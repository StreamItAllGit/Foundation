// @module: Foundation.Tests.FoundationTerminalDrawerTests
// @purpose: Verifies Foundation v0.3.0 (@foundation/ui v0.2.0) canonical ShadowLibrary Strict Standard terminal drawer components, Vanilla ES module WebAssets, <400 LOC budgets, and sync_foundation_sdk.py publishing/consumer sync.
// @depends-on: System, System.Diagnostics, System.IO, System.Text.Json, Foundation.UI, Xunit
// @exports: FoundationTerminalDrawerTests

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Foundation.UI;
using Xunit;

namespace Foundation.Tests;

public sealed class FoundationTerminalDrawerTests
{
    private static string ResolveFoundationRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Foundation.sln")))
            {
                return dir.Parent!.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Unable to locate Foundation repository root containing Dev/Foundation.sln.");
    }

    [Fact]
    public void TerminalDrawer_ReactComponents_And_Core_MeetShadowLibraryStrictStandard_And_LineLimits()
    {
        string root = ResolveFoundationRoot();
        string webRoot = Path.Combine(root, "Dev", "src", "Foundation.UI", "Web");
        string corePath = Path.Combine(webRoot, "components", "terminalDrawerCore.ts");
        string toolbarPath = Path.Combine(webRoot, "components", "TerminalToolbarAndFilters.tsx");
        string drawerPath = Path.Combine(webRoot, "components", "GlobalTerminalDrawer.tsx");
        string indexPath = Path.Combine(webRoot, "index.ts");
        string pkgPath = Path.Combine(webRoot, "package.json");

        Assert.True(File.Exists(corePath));
        Assert.True(File.Exists(toolbarPath));
        Assert.True(File.Exists(drawerPath));
        Assert.True(File.Exists(indexPath));
        Assert.True(File.Exists(pkgPath));

        int coreLoc = File.ReadAllLines(corePath).Length;
        int toolbarLoc = File.ReadAllLines(toolbarPath).Length;
        int drawerLoc = File.ReadAllLines(drawerPath).Length;
        Assert.True(coreLoc <= 200, $"terminalDrawerCore.ts LOC {coreLoc} exceeds 200");
        Assert.True(toolbarLoc <= 290, $"TerminalToolbarAndFilters.tsx LOC {toolbarLoc} exceeds 290");
        Assert.True(drawerLoc <= 320, $"GlobalTerminalDrawer.tsx LOC {drawerLoc} exceeds 320");

        string coreText = File.ReadAllText(corePath);
        Assert.Contains("@module:", coreText);
        Assert.Contains("shouldShowLogLine", coreText);
        Assert.Contains("isReplaceableProgressLine", coreText);
        Assert.Contains("coalesceTerminalLogBatch", coreText);
        Assert.Contains("clampTerminalDrawerHeight", coreText);
        Assert.Contains("computeTerminalDrawerPresetHeight", coreText);
        Assert.Contains("filterTerminalLines", coreText);

        string toolbarText = File.ReadAllText(toolbarPath);
        Assert.Contains("@module:", toolbarText);
        Assert.Contains("Verbosity:", toolbarText);
        Assert.Contains("History:", toolbarText);
        Assert.Contains("Filter logs...", toolbarText);
        Assert.Contains("Wrap lines", toolbarText);
        Assert.Contains("Auto-scroll", toolbarText);
        Assert.Contains("Stop Current Job", toolbarText);
        Assert.Contains("Stop All Jobs", toolbarText);

        string drawerText = File.ReadAllText(drawerPath);
        Assert.Contains("@module:", drawerText);
        Assert.Contains("role=\"separator\"", drawerText);
        Assert.Contains("ResizeObserver", drawerText);
        Assert.Contains("ConfirmActionModal", drawerText);
        Assert.Contains("sl-terminal-docked fn-terminal-docked", drawerText);

        string indexText = File.ReadAllText(indexPath);
        Assert.Contains("GlobalTerminalDrawer", indexText);
        Assert.Contains("GlobalTerminalDrawerProps", indexText);
        Assert.Contains("TerminalToolbarAndFilters", indexText);
        Assert.Contains("TerminalToolbarAndFiltersProps", indexText);
        Assert.Contains("filterTerminalLines", indexText);
        Assert.Contains("TerminalLogFilter", indexText);
        Assert.Contains("coalesceTerminalLogBatch", indexText);

        string pkgText = File.ReadAllText(pkgPath);
        Assert.Contains("\"version\": \"0.5.6\"", pkgText);

        var branding = new ShellBrandingOptions { AppId = "test", DisplayName = "Test" };
        Assert.Equal("0.5.6", branding.SdkVersion);
    }

    [Fact]
    public void TerminalDrawer_VanillaJs_And_Css_Exist_StayUnder400Loc_And_ExportRequiredContracts()
    {
        string root = ResolveFoundationRoot();
        string assetsRoot = Path.Combine(root, "Dev", "src", "Foundation.UI", "WebAssets");
        string cssPath = Path.Combine(assetsRoot, "foundation-terminal.css");
        string jsPath = Path.Combine(assetsRoot, "foundation-terminal.js");

        Assert.True(File.Exists(cssPath));
        Assert.True(File.Exists(jsPath));

        int cssLoc = File.ReadAllLines(cssPath).Length;
        int jsLoc = File.ReadAllLines(jsPath).Length;
        Assert.True(cssLoc <= 220, $"foundation-terminal.css LOC {cssLoc} exceeds 220");
        Assert.True(jsLoc <= 320, $"foundation-terminal.js LOC {jsLoc} exceeds 320");

        string cssText = File.ReadAllText(cssPath);
        Assert.Contains(".fn-terminal-docked", cssText);
        Assert.Contains(".sl-terminal-docked", cssText);
        Assert.Contains(".fn-terminal-resize-handle", cssText);
        Assert.Contains("@media (max-width: 820px), (max-height: 480px)", cssText);
        Assert.Contains("overflow: hidden;", cssText);
        Assert.Contains("min-height: 44px;", cssText);

        string jsText = File.ReadAllText(jsPath);
        Assert.Contains("mountFoundationTerminalDrawer", jsText);
        Assert.Contains("FoundationTerminalDrawer", jsText);
        Assert.Contains("role=\"separator\"", jsText);
        Assert.Contains("Verbosity:", jsText);
        Assert.Contains("History:", jsText);
        Assert.Contains("Stop Current Job", jsText);
        Assert.Contains("Stop All Jobs", jsText);
        Assert.Contains("<span class=\"sl-mobile-only\">Flt</span>", jsText);
        Assert.Contains("window.addEventListener('resize'", jsText);
        Assert.Contains("window.addEventListener('keydown'", jsText);
        Assert.Contains("window.removeEventListener('resize'", jsText);
        Assert.Contains("window.removeEventListener('keydown'", jsText);
    }

    [Fact]
    public void SyncFoundationSdkScript_PublishesCompleteV031AndLatest_And_SyncsConsumerDirectory()
    {
        string root = ResolveFoundationRoot();
        string scriptPath = Path.Combine(root, "Dev", "tools", "sync_foundation_sdk.py");
        Assert.True(File.Exists(scriptPath));
        Assert.True(File.ReadAllLines(scriptPath).Length <= 260);

        string tempConsumer = Path.Combine(Path.GetTempPath(), "FoundationConsumerTest_" + Guid.NewGuid().ToString("N")[..8]);
        string tempSharedLibs = Path.Combine(Path.GetTempPath(), "FoundationSharedLibsTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(tempConsumer, "Dev", "lib", "Foundation", "tools"));
        string staleVerifyTool = Path.Combine(tempConsumer, "Dev", "lib", "Foundation", "tools", "verify_sandbox_universal.py");
        string staleSandboxDll = Path.Combine(tempConsumer, "Dev", "lib", "Foundation.Sandbox.dll");
        string staleVisibleCs = Path.Combine(tempConsumer, "Dev", "lib", "VisibleTestWindowHelper.cs");
        string keptSandboxCtl = Path.Combine(tempConsumer, "Dev", "lib", "Foundation", "tools", "sandbox_ctl.py");
        string keptRunE2e = Path.Combine(tempConsumer, "Dev", "lib", "Foundation", "tools", "run_sandbox_e2e.py");
        File.WriteAllText(staleVerifyTool, "# stale");
        File.WriteAllText(staleSandboxDll, "stale");
        File.WriteAllText(staleVisibleCs, "// stale");
        File.WriteAllText(keptSandboxCtl, "# keep");
        File.WriteAllText(keptRunE2e, "# keep");
        try
        {
            var psi = new ProcessStartInfo("python")
            {
                ArgumentList =
                {
                    scriptPath,
                    "--version", "0.5.6",
                    "--ui-version", "0.5.6",
                    "--publish",
                    "--sync-consumer", tempConsumer
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.Environment["FOUNDATION_SHARED_LIBS_ROOT"] = tempSharedLibs;

            using var proc = Process.Start(psi)!;
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(30000);
            Assert.True(proc.ExitCode == 0, $"sync_foundation_sdk.py failed: {stderr}\n{stdout}");

            foreach (string sharedDir in new[]
            {
                Path.Combine(tempSharedLibs, "v0.5.6"),
                Path.Combine(tempSharedLibs, "Latest")
            })
            {
                Assert.True(File.Exists(Path.Combine(sharedDir, "Foundation.UI.dll")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "Foundation.Automation.dll")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "WebAssets", "foundation-terminal.js")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "WebAssets", "foundation-terminal.css")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "WebAssets", "components", "GlobalTerminalDrawer.tsx")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "foundation_core", "__init__.py")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "foundation_core", "automation.py")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "foundation_core", "secrets.py")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "foundation_core", "sidecars.py")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "foundation_core", "sidecars_locks.py")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "foundation_core", "sandbox_seam.py")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "foundation_core", "pseudo_registry.py")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "tools", "sync_foundation_sdk.py")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "tools", "sandbox_ctl.py")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "tools", "run_sandbox_e2e.py")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "tools", "ExeboxStreamHelpers.ps1")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "tools", "exebox_stream.py")));
                Assert.True(File.Exists(Path.Combine(sharedDir, "sdk-manifest.json")));

                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(sharedDir, "sdk-manifest.json")));
                Assert.Equal("0.5.6", doc.RootElement.GetProperty("version").GetString());
                Assert.Equal("0.5.6", doc.RootElement.GetProperty("uiVersion").GetString());
            }

            Assert.True(File.Exists(Path.Combine(tempConsumer, "Dev", "lib", "Foundation.UI.dll")));
            Assert.True(File.Exists(Path.Combine(tempConsumer, "Dev", "lib", "WebAssets", "foundation-terminal.js")));
            Assert.True(File.Exists(Path.Combine(tempConsumer, "Dev", "lib", "WebAssets", "components", "GlobalTerminalDrawer.tsx")));
            Assert.True(File.Exists(Path.Combine(tempConsumer, "Dev", "lib", "foundation_core", "__init__.py")));
            Assert.True(File.Exists(Path.Combine(tempConsumer, "Dev", "lib", "foundation_core", "automation.py")));
            Assert.True(File.Exists(Path.Combine(tempConsumer, "Dev", "lib", "foundation_core", "sandbox_seam.py")));
            Assert.True(File.Exists(Path.Combine(tempConsumer, "Dev", "lib", "foundation_core", "pseudo_registry.py")));
            Assert.True(File.Exists(Path.Combine(tempConsumer, "Dev", "lib", "Foundation", "foundation_core", "sandbox_seam.py")));
            Assert.True(File.Exists(keptSandboxCtl));
            Assert.True(File.Exists(keptRunE2e));
            Assert.False(File.Exists(staleVerifyTool));
            Assert.False(File.Exists(staleSandboxDll));
            Assert.False(File.Exists(staleVisibleCs));
        }
        finally
        {
            try { Directory.Delete(tempConsumer, recursive: true); } catch { }
            try { Directory.Delete(tempSharedLibs, recursive: true); } catch { }
        }
    }
}
