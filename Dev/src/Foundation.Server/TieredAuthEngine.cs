// @module: Foundation.Server.TieredAuthEngine
// @purpose: Shared Kestrel tiered authentication evaluator (loopback bypass, Tailscale CGNAT/ULA, PBKDF2-SHA256, HMAC session cookie, Bearer token).
// @depends-on: System, System.IO, System.Net, System.Security.Cryptography, System.Text, Microsoft.AspNetCore.Http
// @exports: TieredAuthConfigSnapshot, TieredAuthEvaluation, TieredAuthEngine

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Foundation.Server;

public sealed record TieredAuthConfigSnapshot(
    bool TrustLocalhost,
    bool TrustTailscaleServeHeaders,
    bool RequireRemotePassword,
    string AdminPasswordHash,
    string AdminPasswordSalt,
    string ApiToken,
    string CustomTokenHeaderName = "X-ShadowLibrary-Token"
);

public sealed class TieredAuthEvaluation
{
    public bool IsAuthorized { get; set; }
    public string IdentityMode { get; set; } = "Anonymous";
    public string UserDisplay { get; set; } = "Local Operator";
    public bool IsLoopback { get; set; }
    public bool IsTailscale { get; set; }
    public bool RequiresInitialPasswordSetup { get; set; }
}

public static class TieredAuthEngine
{
    public static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromDays(30);

    public static byte[] LoadOrCreateSigningKey(string directoryPath, string keyFileName = ".sl_cookie_secret.key")
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            string keyPath = Path.Combine(directoryPath, keyFileName);
            if (File.Exists(keyPath))
            {
                string hex = File.ReadAllText(keyPath, Encoding.UTF8).Trim();
                if (hex.Length >= 64)
                    return Convert.FromHexString(hex[..64]);
            }

            byte[] random = RandomNumberGenerator.GetBytes(32);
            File.WriteAllText(keyPath, Convert.ToHexString(random), Encoding.UTF8);
            return random;
        }
        catch
        {
            return RandomNumberGenerator.GetBytes(32);
        }
    }

    public static (string HashHex, string SaltHex) HashPassword(string plainPassword)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(plainPassword),
            salt,
            iterations: 100_000,
            HashAlgorithmName.SHA256,
            outputLength: 32);
        return (Convert.ToHexString(hash), Convert.ToHexString(salt));
    }

    public static bool VerifyPassword(string plainPassword, string storedHashHex, string storedSaltHex)
    {
        if (string.IsNullOrWhiteSpace(storedHashHex) || string.IsNullOrWhiteSpace(storedSaltHex))
            return false;

        try
        {
            byte[] salt = Convert.FromHexString(storedSaltHex);
            byte[] expected = Convert.FromHexString(storedHashHex);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(plainPassword),
                salt,
                iterations: 100_000,
                HashAlgorithmName.SHA256,
                outputLength: expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }

    public static string CreateSessionCookieValue(byte[] hmacSecret, TimeSpan? lifetime = null, string identity = "sladmin")
    {
        long expUnix = DateTimeOffset.UtcNow.Add(lifetime ?? DefaultSessionLifetime).ToUnixTimeSeconds();
        string safeIdentity = string.IsNullOrWhiteSpace(identity) ? "sladmin" : identity.Replace(":", "_").Replace(".", "_");
        string payload = $"{safeIdentity}:{expUnix}";
        byte[] sig = HMACSHA256.HashData(hmacSecret, Encoding.UTF8.GetBytes(payload));
        return $"{payload}.{Convert.ToHexString(sig).ToLowerInvariant()}";
    }

    public static bool ValidateSessionCookieValue(byte[] hmacSecret, string? cookieValue)
    {
        if (string.IsNullOrWhiteSpace(cookieValue))
            return false;

        int dot = cookieValue.LastIndexOf('.');
        if (dot <= 0 || dot >= cookieValue.Length - 1)
            return false;

        string payload = cookieValue[..dot];
        string sigHex = cookieValue[(dot + 1)..];

        try
        {
            byte[] expectedSig = HMACSHA256.HashData(hmacSecret, Encoding.UTF8.GetBytes(payload));
            byte[] actualSig = Convert.FromHexString(sigHex);
            if (!CryptographicOperations.FixedTimeEquals(expectedSig, actualSig))
                return false;

            string[] parts = payload.Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[1], out long expUnix))
                return false;

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= expUnix;
        }
        catch
        {
            return false;
        }
    }

    public static void IssueSessionCookie(HttpContext ctx, string cookieName, byte[] hmacSecret, string identity = "sladmin", TimeSpan? lifetime = null)
    {
        bool isHttps = ctx.Request.IsHttps ||
                       string.Equals(ctx.Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);

        TimeSpan effectiveLifetime = lifetime ?? DefaultSessionLifetime;
        string token = CreateSessionCookieValue(hmacSecret, effectiveLifetime, identity);
        ctx.Response.Cookies.Append(cookieName, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = isHttps,
            MaxAge = effectiveLifetime,
            Expires = DateTimeOffset.UtcNow.Add(effectiveLifetime),
            Path = "/"
        });
    }

    public static void ClearSessionCookie(HttpContext ctx, string cookieName)
    {
        ctx.Response.Cookies.Delete(cookieName, new CookieOptions { Path = "/" });
    }

    public static TieredAuthEvaluation EvaluateRequest(
        HttpContext ctx,
        TieredAuthConfigSnapshot cfg,
        byte[] hmacSecret,
        string sessionCookieName = "sl_session",
        IPAddress? simulatedRemoteIp = null)
    {
        IPAddress? remoteIp = simulatedRemoteIp ?? ctx.Connection.RemoteIpAddress;
        bool hasForwardedFor = ctx.Request.Headers.ContainsKey("X-Forwarded-For");

        bool isRawLoopback = remoteIp == null || IPAddress.IsLoopback(remoteIp) ||
                             (remoteIp.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remoteIp.MapToIPv4()));
        bool isDirectLoopback = isRawLoopback && !hasForwardedFor;
        bool isTailscaleIp = remoteIp != null && (IsTailscaleCgnatIpv4(remoteIp) || IsTailscaleUlaIpv6(remoteIp));
        bool hasPassword = !string.IsNullOrWhiteSpace(cfg.AdminPasswordHash) && !string.IsNullOrWhiteSpace(cfg.AdminPasswordSalt);

        if (!string.IsNullOrWhiteSpace(cfg.ApiToken))
        {
            string? suppliedToken = null;
            if (!string.IsNullOrWhiteSpace(cfg.CustomTokenHeaderName) &&
                ctx.Request.Headers.TryGetValue(cfg.CustomTokenHeaderName, out var xTok) &&
                !string.IsNullOrWhiteSpace(xTok))
            {
                suppliedToken = xTok.ToString().Trim();
            }
            else if (ctx.Request.Headers.TryGetValue("Authorization", out var authHdr))
            {
                string rawAuth = authHdr.ToString();
                if (rawAuth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    suppliedToken = rawAuth["Bearer ".Length..].Trim();
            }

            if (!string.IsNullOrEmpty(suppliedToken) && ConstantTimeEquals(suppliedToken, cfg.ApiToken))
            {
                return new TieredAuthEvaluation
                {
                    IsAuthorized = true, IdentityMode = "ApiToken", UserDisplay = "API / MCP Client",
                    IsLoopback = isDirectLoopback, IsTailscale = isTailscaleIp
                };
            }
        }

        if (isRawLoopback && cfg.TrustTailscaleServeHeaders &&
            ctx.Request.Headers.TryGetValue("Tailscale-User-Login", out var tsLogin) &&
            !string.IsNullOrWhiteSpace(tsLogin))
        {
            return new TieredAuthEvaluation
            {
                IsAuthorized = true, IdentityMode = "TailscaleServe", UserDisplay = tsLogin.ToString().Trim(),
                IsLoopback = false, IsTailscale = true
            };
        }

        if (isDirectLoopback && cfg.TrustLocalhost)
        {
            return new TieredAuthEvaluation
            {
                IsAuthorized = true, IdentityMode = "Localhost", UserDisplay = $"{Environment.MachineName}\\{Environment.UserName}",
                IsLoopback = true, IsTailscale = false, RequiresInitialPasswordSetup = cfg.RequireRemotePassword && !hasPassword
            };
        }

        if (ctx.Request.Cookies.TryGetValue(sessionCookieName, out string? cookieVal) &&
            ValidateSessionCookieValue(hmacSecret, cookieVal))
        {
            return new TieredAuthEvaluation
            {
                IsAuthorized = true, IdentityMode = "SessionCookie",
                UserDisplay = isTailscaleIp ? "Tailscale Admin" : "Authenticated Admin",
                IsLoopback = isDirectLoopback, IsTailscale = isTailscaleIp
            };
        }

        string accessTokenQuery = ctx.Request.Query["access_token"].ToString();
        if (!string.IsNullOrEmpty(accessTokenQuery) &&
            ((!string.IsNullOrWhiteSpace(cfg.ApiToken) && ConstantTimeEquals(accessTokenQuery, cfg.ApiToken)) ||
             ValidateSessionCookieValue(hmacSecret, accessTokenQuery)))
        {
            return new TieredAuthEvaluation
            {
                IsAuthorized = true, IdentityMode = "SignalRQueryToken", UserDisplay = "SignalR Client",
                IsLoopback = isDirectLoopback, IsTailscale = isTailscaleIp
            };
        }

        if (!hasPassword && !cfg.RequireRemotePassword)
        {
            return new TieredAuthEvaluation
            {
                IsAuthorized = true, IdentityMode = isTailscaleIp ? "TailscaleDirect" : "OpenLan",
                UserDisplay = isTailscaleIp ? "Tailscale Operator" : "LAN Operator",
                IsLoopback = isDirectLoopback, IsTailscale = isTailscaleIp
            };
        }

        return new TieredAuthEvaluation
        {
            IsAuthorized = false, IdentityMode = !hasPassword ? "SetupRequired" : "Unauthorized",
            UserDisplay = "Anonymous", IsLoopback = isDirectLoopback, IsTailscale = isTailscaleIp,
            RequiresInitialPasswordSetup = !hasPassword
        };
    }

    public static bool IsTailscaleCgnatIpv4(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = ip.GetAddressBytes();
        return b.Length == 4 && b[0] == 100 && (b[1] & 0xC0) == 64;
    }

    public static bool IsTailscaleUlaIpv6(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return false;
        byte[] b = ip.GetAddressBytes();
        return b.Length == 16 && b[0] == 0xFD && b[1] == 0x7A && b[2] == 0x11 && b[3] == 0x5C && b[4] == 0xA1 && b[5] == 0xE0;
    }

    public static bool ConstantTimeEquals(string a, string b)
    {
        byte[] ba = Encoding.UTF8.GetBytes(a);
        byte[] bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
