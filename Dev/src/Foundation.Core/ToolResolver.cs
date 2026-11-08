// @module: Foundation.Core.Tools
// @purpose: Implements 4-tier external tool discovery (settings override -> app\tools\ -> PATH -> graceful fallback).
// @depends-on: System, System.Collections.Generic, System.IO
// @exports: ToolResolutionSource, ToolResolutionResult, ToolResolver

using System;
using System.Collections.Generic;
using System.IO;

namespace Foundation.Core;

public enum ToolResolutionSource
{
    ExplicitOverride,
    BundledAppTools,
    SystemPath,
    MissingFallback
}

public sealed record ToolResolutionResult(
    string ToolId,
    bool IsAvailable,
    string? ResolvedPath,
    ToolResolutionSource Source,
    string DiagnosticMessage
);

/// <summary>
/// Resolves external binaries (7z, chdman, flips, xdelta3, rclone, etc.) using the Dev Bible 4-tier hierarchy:
/// 1. Explicit settings.json override
/// 2. Bundled app\tools\ (or BaseDirectory\tools\)
/// 3. System PATH (or standard Program Files folders)
/// 4. Graceful degradation (never throwing an unhandled startup exception)
/// </summary>
public static class ToolResolver
{
    public static ToolResolutionResult ResolveTool(
        string toolId,
        IEnumerable<string> candidateExecutableNames,
        string? explicitOverridePath = null,
        string? baseDirectory = null,
        IEnumerable<string>? extraSearchDirectories = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        ArgumentNullException.ThrowIfNull(candidateExecutableNames);

        // Tier 1: Explicit settings override
        if (!string.IsNullOrWhiteSpace(explicitOverridePath))
        {
            string trimmedOverride = explicitOverridePath.Trim().Trim('"');
            if (File.Exists(trimmedOverride))
            {
                string full = Path.GetFullPath(trimmedOverride);
                return new ToolResolutionResult(
                    toolId,
                    true,
                    full,
                    ToolResolutionSource.ExplicitOverride,
                    $"Resolved '{toolId}' via explicit override: {full}");
            }
        }

        string rootDir = !string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.GetFullPath(baseDirectory)
            : AppContext.BaseDirectory;

        string[] bundledDirs =
        {
            Path.Combine(rootDir, "tools"),
            Path.Combine(rootDir, "app", "tools"),
            rootDir
        };

        // Tier 2: Bundled app\tools\
        foreach (string dir in bundledDirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (string exeName in candidateExecutableNames)
            {
                string candidate = Path.Combine(dir, exeName);
                if (File.Exists(candidate))
                {
                    string full = Path.GetFullPath(candidate);
                    return new ToolResolutionResult(
                        toolId,
                        true,
                        full,
                        ToolResolutionSource.BundledAppTools,
                        $"Resolved '{toolId}' from bundled tools directory: {full}");
                }
            }
        }

        // Tier 3: System PATH and optional well-known directories
        var searchDirs = new List<string>();
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (string entry in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string cleaned = entry.Trim().Trim('"');
                if (!string.IsNullOrEmpty(cleaned))
                {
                    searchDirs.Add(cleaned);
                }
            }
        }

        if (extraSearchDirectories is not null)
        {
            foreach (string extra in extraSearchDirectories)
            {
                if (!string.IsNullOrWhiteSpace(extra))
                {
                    searchDirs.Add(extra.Trim());
                }
            }
        }

        foreach (string dir in searchDirs)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (string exeName in candidateExecutableNames)
                {
                    string candidate = Path.Combine(dir, exeName);
                    if (File.Exists(candidate))
                    {
                        string full = Path.GetFullPath(candidate);
                        return new ToolResolutionResult(
                            toolId,
                            true,
                            full,
                            ToolResolutionSource.SystemPath,
                            $"Resolved '{toolId}' from system PATH: {full}");
                    }
                }
            }
            catch
            {
                // Ignore inaccessible PATH entries
            }
        }

        // Tier 4: Graceful degradation
        return new ToolResolutionResult(
            toolId,
            false,
            null,
            ToolResolutionSource.MissingFallback,
            $"Optional tool '{toolId}' was not found in override, app\\tools\\, or PATH; dependent features will degrade gracefully.");
    }
}
