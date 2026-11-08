// @module: Foundation.Server.TieredAuthMiddleware
// @purpose: Shared ASP.NET Core route protection helper for /api/*, /hubs/*, and /mcp/* with PWA shell exemptions.
// @depends-on: System, System.Threading.Tasks, Microsoft.AspNetCore.Http
// @exports: FoundationTieredAuthMiddleware

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Foundation.Server;

public static class FoundationTieredAuthMiddleware
{
    public static bool IsProtectedRoute(string path) =>
        path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/mcp", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/mcp/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/mcp-sse", StringComparison.OrdinalIgnoreCase);

    public static bool IsPublicApiEndpoint(string path) =>
        path.Equals("/api/v1/health", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/v1/auth/status", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/v1/auth/logout", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/v1/auth/setup-password", StringComparison.OrdinalIgnoreCase);

    public static async Task<bool> WriteUnauthorizedIfBlockedAsync(
        HttpContext context,
        string path,
        bool isAuthorized,
        bool requiresInitialPasswordSetup,
        string appDisplayName = "ShadowLibrary")
    {
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.CacheControl = "no-store";
        }

        if (!IsProtectedRoute(path) || IsPublicApiEndpoint(path) || isAuthorized)
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            error = requiresInitialPasswordSetup
                ? $"First-time remote access requires configuring a {appDisplayName} Admin Password."
                : $"Authentication required. Please sign in with your {appDisplayName} Admin Password.",
            requiresAuth = true,
            requiresSetup = requiresInitialPasswordSetup
        });
        return true;
    }
}
