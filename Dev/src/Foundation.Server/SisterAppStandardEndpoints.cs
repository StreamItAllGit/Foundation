// @module: Foundation.Server.SisterAppStandardEndpoints
// @purpose: Standard 17 & Standard 18 suite.manifest.json v2 loader and Sister-App endpoint mapper.
// @depends-on: System, System.IO, System.Text, System.Text.Json, Microsoft.AspNetCore.Builder, Microsoft.AspNetCore.Http, Microsoft.AspNetCore.Routing
// @exports: SisterAppStandardEndpoints

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Foundation.Server;

public static class SisterAppStandardEndpoints
{
    public static object LoadOrBuildSuiteManifestObject(Func<object> fallbackFactory, string? baseDirectory = null)
    {
        string rootDir = baseDirectory ?? AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(rootDir, "suite.manifest.json"),
            Path.Combine(rootDir, "..", "suite.manifest.json")
        };

        foreach (string path in candidates)
        {
            if (File.Exists(path))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                    return doc.RootElement.Clone();
                }
                catch { }
            }
        }

        return fallbackFactory();
    }

    public static void MapSuiteManifestEndpoints(
        WebApplication app,
        RouteGroupBuilder apiV1Group,
        Func<object> fallbackFactory)
    {
        apiV1Group.MapGet("/suite/manifest", () => Results.Ok(LoadOrBuildSuiteManifestObject(fallbackFactory)));
        app.MapGet("/api/suite/manifest", () => Results.Ok(LoadOrBuildSuiteManifestObject(fallbackFactory)));
        app.MapGet("/suite.manifest.json", () => Results.Ok(LoadOrBuildSuiteManifestObject(fallbackFactory)));
    }
}
