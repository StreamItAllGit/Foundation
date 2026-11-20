// @module: Foundation.Core.Networking.Validation
// @purpose: Standard 41 URI scheme, HTTPS-only, SSRF/cloud-metadata IP & DNS resolution validation, TLS 1.2+, and bounded redirect handling for SafeNetworkTransfer.
// @depends-on: System, System.IO, System.Net, System.Net.Http, System.Net.Sockets, System.Security.Authentication, System.Threading, System.Threading.Tasks
// @exports: SafeNetworkTransfer (partial: ValidateRequestUri, IsBlockedSsrfAddress, SendWithValidatedRedirectsAsync)

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace Foundation.Core;

public static partial class SafeNetworkTransfer
{
    private static readonly IPAddress AwsMetadataIpv6 = IPAddress.Parse("fd00:ec2::254");

    public static void ValidateRequestUri(
        Uri requestUri,
        bool requireHttps = false,
        bool allowLoopbackHttp = false)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        if (!requestUri.IsAbsoluteUri)
        {
            throw new InvalidOperationException($"Blocked relative URI '{requestUri}'. Only absolute HTTP/HTTPS URIs are allowed.");
        }

        string scheme = requestUri.Scheme;
        bool isHttps = string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        bool isHttp = string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !isHttp)
        {
            throw new InvalidOperationException($"Blocked unsafe URI scheme '{scheme}'. Only HTTP/HTTPS allowed.");
        }

        string host = requestUri.Host.Trim().Trim('[', ']');
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Blocked URI with empty host.");
        }

        if (host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("metadata.goog", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".metadata.google.internal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Blocked cloud metadata SSRF host '{requestUri.Host}'.");
        }

        if (IPAddress.TryParse(host, out IPAddress? parsedIp) && IsBlockedSsrfAddress(parsedIp))
        {
            throw new InvalidOperationException($"Blocked link-local or cloud metadata SSRF host '{requestUri.Host}'.");
        }

        if (requireHttps && !isHttps)
        {
            bool isLoopback = allowLoopbackHttp && (
                host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                (parsedIp is not null && IPAddress.IsLoopback(parsedIp)));
            if (!isLoopback)
            {
                throw new InvalidOperationException($"Blocked non-HTTPS URI '{requestUri}'. Only https:// is allowed.");
            }
        }
    }

    public static bool IsBlockedSsrfAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        IPAddress ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            return b[0] == 169 && b[1] == 254;
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.Equals(AwsMetadataIpv6);
        }
        return false;
    }

    private static HttpClient CreateHttpClient(TimeSpan connectTimeout, HttpMessageHandler? customHandler)
    {
        if (customHandler is not null)
        {
            return new HttpClient(customHandler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        }

        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = connectTimeout,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            },
            ConnectCallback = ConnectWithSsrfGuardAsync
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static async ValueTask<Stream> ConnectWithSsrfGuardAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        string host = context.DnsEndPoint.Host.Trim().Trim('[', ']');
        IPAddress[] targets;
        if (IPAddress.TryParse(host, out IPAddress? literalIp))
        {
            targets = new[] { literalIp };
        }
        else
        {
            targets = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            if (targets.Length == 0)
            {
                throw new IOException($"DNS resolution returned no addresses for '{host}'.");
            }
        }

        foreach (IPAddress addr in targets)
        {
            if (IsBlockedSsrfAddress(addr))
            {
                throw new InvalidOperationException($"Blocked SSRF connection to resolved address '{addr}' for host '{host}'.");
            }
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(targets, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<HttpResponseMessage> SendWithValidatedRedirectsAsync(
        HttpClient client,
        Uri initialUri,
        long existingOffset,
        SafeTransferOptions opts,
        CancellationToken cancellationToken)
    {
        Uri currentUri = initialUri;
        int maxRedirects = Math.Clamp(opts.MaxRedirects, 0, 20);
        int redirectCount = 0;

        while (true)
        {
            ValidateRequestUri(currentUri, opts.RequireHttps, opts.AllowLoopbackHttpForTesting);

            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            if (existingOffset > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existingOffset, null);
            }

            TimeSpan headerTimeout = opts.ResponseHeadersTimeout ?? opts.ReadStallTimeout;
            using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (headerTimeout > TimeSpan.Zero && headerTimeout != Timeout.InfiniteTimeSpan)
            {
                headerCts.CancelAfter(headerTimeout);
            }

            HttpResponseMessage response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, headerCts.Token).ConfigureAwait(false);

            int statusCode = (int)response.StatusCode;
            if (statusCode is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location)
            {
                Uri nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                response.Dispose();

                redirectCount++;
                if (redirectCount > maxRedirects)
                {
                    throw new InvalidOperationException($"Blocked transfer: exceeded maximum redirect limit ({maxRedirects}).");
                }

                ValidateRequestUri(nextUri, opts.RequireHttps, opts.AllowLoopbackHttpForTesting);
                currentUri = nextUri;
                continue;
            }

            return response;
        }
    }
}
