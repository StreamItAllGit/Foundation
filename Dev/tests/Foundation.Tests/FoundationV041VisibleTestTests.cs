// @module: Foundation.Tests.V041VisibleTest
// @purpose: Verifies v0.4.1 Standard 02 visible-by-default --test-mode, --headless override, Z-order bitmap capture, wizard manifest forwarding, UI helper, and <= 400 LOC compliance.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Foundation.Core;
using Foundation.Installer;
using Foundation.UI.Desktop;
using Xunit;

namespace Foundation.Tests;

public sealed class FoundationV041VisibleTestTests : IDisposable
{
    private readonly string _tempRoot;

    public FoundationV041VisibleTestTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "FoundationV041_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void SandboxEnvironment_TestModeDefaultsVisible_And_HeadlessOverrides()
    {
        string dataDir = Path.Combine(_tempRoot, "Sandbox", "data");

        var testDefault = new SandboxEnvironment("restation", new[] { "--test-mode", "--data-dir", dataDir });
        Assert.True(testDefault.TestMode);
        Assert.False(testDefault.Headless);
        Assert.False(testDefault.ExplicitVisibleTest);
        Assert.True(testDefault.VisibleTest);
        Assert.True(testDefault.EffectiveVisibleTest);
        Assert.Equal(UiPresentationMode.DesktopWindow, testDefault.PresentationMode);

        var headlessOverride = new SandboxEnvironment("restation", new[] { "--test-mode", "--headless", "--data-dir", dataDir });
        Assert.True(headlessOverride.TestMode);
        Assert.True(headlessOverride.Headless);
        Assert.False(headlessOverride.VisibleTest);
        Assert.False(headlessOverride.EffectiveVisibleTest);
        Assert.Equal(UiPresentationMode.HeadlessOrTrayOnly, headlessOverride.PresentationMode);

        var compatBoth = new SandboxEnvironment("restation", new[] { "--headless", "--visible-test", "--data-dir", dataDir });
        Assert.True(compatBoth.VisibleTest);
        Assert.True(compatBoth.ExplicitVisibleTest);
        Assert.True(compatBoth.Headless);
        Assert.False(compatBoth.EffectiveVisibleTest);
        Assert.Equal(UiPresentationMode.HeadlessOrTrayOnly, compatBoth.PresentationMode);

        var testBrowser = new SandboxEnvironment("restation", new[] { "--test-mode", "--browser", "--data-dir", dataDir });
        Assert.True(testBrowser.VisibleTest);
        Assert.Equal(UiPresentationMode.LocalBrowser, testBrowser.PresentationMode);

        var testTray = new SandboxEnvironment("restation", new[] { "--test-mode", "--tray-only", "--data-dir", dataDir });
        Assert.False(testTray.VisibleTest);
        Assert.False(testTray.EffectiveVisibleTest);
        Assert.Equal(UiPresentationMode.HeadlessOrTrayOnly, testTray.PresentationMode);

        string? prevHeadless = Environment.GetEnvironmentVariable("HEADLESS");
        try
        {
            Environment.SetEnvironmentVariable("HEADLESS", "1");
            var envHeadless = new SandboxEnvironment("restation", new[] { "--test-mode", "--data-dir", dataDir });
            Assert.True(envHeadless.Headless);
            Assert.False(envHeadless.EffectiveVisibleTest);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HEADLESS", prevHeadless);
        }
    }

    [Fact]
    public void Python_FoundationCore_And_Tools_VerifyVisibleDefaultAndVersions()
    {
        string devRoot = Path.Combine(ResolveFoundationRoot(), "Dev");
        string srcDir = Path.Combine(devRoot, "src").Replace("\\", "\\\\");
        string toolsDir = Path.Combine(devRoot, "tools").Replace("\\", "\\\\");

        string pyScript = $@"
import sys
sys.path.insert(0, r'{srcDir}')
sys.path.insert(0, r'{toolsDir}')
import foundation_core
from foundation_core.cli import parse_standard_cli_flags
from verify_sandbox_universal import build_browser_launch_command
assert foundation_core.SDK_VERSION == '0.4.1', foundation_core.SDK_VERSION
o1 = parse_standard_cli_flags(['--test-mode', '--data-dir', r'{_tempRoot}\py1', '--log-dir', r'{_tempRoot}\py1\logs'])
assert o1.test_mode and not o1.headless and o1.visible_test and o1.effective_visible_test
o2 = parse_standard_cli_flags(['--test-mode', '--headless', '--data-dir', r'{_tempRoot}\py2', '--log-dir', r'{_tempRoot}\py2\logs'])
assert o2.headless and not o2.visible_test and not o2.effective_visible_test
o3 = parse_standard_cli_flags(['--headless', '--visible-test', '--data-dir', r'{_tempRoot}\py3', '--log-dir', r'{_tempRoot}\py3\logs'])
assert o3.headless and o3.visible_test and not o3.effective_visible_test
cmd_vis = build_browser_launch_command('msedge.exe', 'http://127.0.0.1:5179', r'{_tempRoot}\prof', headless=False)
assert '--new-window' in cmd_vis and '--headless=new' not in cmd_vis
cmd_hl = build_browser_launch_command('msedge.exe', 'http://127.0.0.1:5179', r'{_tempRoot}\prof', headless=True)
assert '--headless=new' in cmd_hl
print('PY_OK')
";
        var psi = new ProcessStartInfo("python")
        {
            ArgumentList = { "-c", pyScript },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(15000);
        Assert.True(proc.ExitCode == 0, $"Python check failed: {stderr}\n{stdout}");
        Assert.Contains("PY_OK", stdout);

        Assert.Contains("version = \"0.4.1\"", File.ReadAllText(Path.Combine(devRoot, "src", "foundation_core", "pyproject.toml")));
        Assert.Contains("<Version>0.4.1</Version>", File.ReadAllText(Path.Combine(devRoot, "Directory.Build.props")));
        Assert.Contains("\"version\": \"0.4.1\"", File.ReadAllText(Path.Combine(devRoot, "app.manifest.json")));
    }

    [Fact]
    public void InstallerWizardForm_CapturePreviewBitmap_RendersControlsAboveBody_And_DecouplesLegacy()
    {
        string sandboxRoot = Path.Combine(_tempRoot, "WizardPreview");
        string dataDir = Path.Combine(sandboxRoot, "data");
        string logDir = Path.Combine(sandboxRoot, "logs");
        var ctx = new InstallerSandboxContext(cliDataDir: dataDir, cliLogDir: logDir, testMode: true, envPrefix: "PRV", cliSandboxRoot: sandboxRoot);
        var desc = new InstallerAppDescriptor("PreviewApp", "Preview App", "Studio", "0.4.1", AllowLegacyMigration: true);
        var engine = new InstallerEngine(desc, ctx);

        using var wizard = new InstallerWizardForm(
            engine,
            ctx,
            initialInstallDir: Path.Combine(sandboxRoot, "app"),
            externalPayloadPath: null,
            isUninstallMode: false,
            initialCreateShortcuts: true,
            initialAutoStart: false,
            initialLaunchAfter: true,
            initialPurgeData: false,
            showShowcaseAutoCloseMs: 0,
            initialProfileId: "lightweight",
            initialSecondaryDir: Path.Combine(sandboxRoot, "reEngine"),
            secondaryFlagName: "--reengine-dir",
            initialMigrateLegacy: false,
            showLegacyMigration: true);

        wizard.Show();
        Application.DoEvents();
        Assert.True(wizard.MigrateLegacyCheckBox.Visible);
        Assert.False(wizard.MigrateLegacyCheckBox.Checked);
        Assert.False(wizard.MigrateLegacyChecked);

        using Bitmap bmp = wizard.CaptureWizardPreviewBitmap();
        Assert.Equal(wizard.Width, bmp.Width);
        Assert.Equal(wizard.Height, bmp.Height);

        int nonBgPixels = 0;
        Point clientTopLeft = wizard.PointToScreen(Point.Empty);
        int offX = Math.Clamp(clientTopLeft.X - wizard.Bounds.X, 0, Math.Max(0, bmp.Width - wizard.ClientSize.Width));
        int offY = Math.Clamp(clientTopLeft.Y - wizard.Bounds.Y, 0, Math.Max(0, bmp.Height - wizard.ClientSize.Height));
        Rectangle optBounds = wizard.OptionsPanel.Bounds;
        for (int y = offY + optBounds.Top + 4; y < Math.Min(bmp.Height, offY + optBounds.Bottom - 4); y += 6)
        {
            for (int x = offX + optBounds.Left + 4; x < Math.Min(bmp.Width, offX + optBounds.Right - 4); x += 8)
            {
                Color c = bmp.GetPixel(x, y);
                if (c.R != 14 || c.G != 17 || c.B != 23)
                    nonBgPixels++;
            }
        }
        wizard.Close();
        Assert.True(nonBgPixels > 20, $"Expected visible non-background pixels inside OptionsPanel, got {nonBgPixels}");
    }

    [Fact]
    public void InstallerWizardForm_ForwardsProfileSecondaryDirAndLegacyToInstallManifest()
    {
        string sandboxRoot = Path.Combine(_tempRoot, "WizardForward");
        string dataDir = Path.Combine(sandboxRoot, "data");
        string logDir = Path.Combine(sandboxRoot, "logs");
        string installDir = Path.Combine(sandboxRoot, "installed-app");
        string customReEngine = Path.Combine(sandboxRoot, "reEngineCustom");
        string payloadZip = BuildPayloadZip("ForwardApp.exe");

        var ctx = new InstallerSandboxContext(cliDataDir: dataDir, cliLogDir: logDir, testMode: true, visibleTest: true, envPrefix: "FWD", cliSandboxRoot: sandboxRoot);
        var desc = new InstallerAppDescriptor("ForwardApp", "Forward App", "Studio", "0.4.1", UmbrellaSubpath: "Studio/Suite/ForwardApp", AllowLegacyMigration: true);
        var engine = new InstallerEngine(desc, ctx);

        using var wizard = new InstallerWizardForm(
            engine,
            ctx,
            initialInstallDir: installDir,
            externalPayloadPath: payloadZip,
            isUninstallMode: false,
            initialCreateShortcuts: false,
            initialAutoStart: false,
            initialLaunchAfter: false,
            initialPurgeData: false,
            showShowcaseAutoCloseMs: 0,
            initialProfileId: "lightweight",
            initialSecondaryDir: Path.Combine(sandboxRoot, "reEngineInitial"),
            secondaryFlagName: "--reengine-dir",
            initialMigrateLegacy: false,
            showLegacyMigration: true,
            autoRunVisibleTest: true);

        wizard.FullProfileRadioButton.Checked = true;
        wizard.SecondaryDirTextBox.Text = customReEngine;
        wizard.MigrateLegacyCheckBox.Checked = true;

        Application.Run(wizard);
        Assert.Equal(0, wizard.ExitCode);
        Assert.True(File.Exists(wizard.LastVisibleTestScreenshotPath));

        string manifestPath = Path.Combine(installDir, InstallerEngine.InstallManifestFileName);
        Assert.True(File.Exists(manifestPath));
        InstallManifest manifest = JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(manifestPath))!;
        Assert.Equal("full", manifest.InstallProfile);
        Assert.Equal(Path.GetFullPath(customReEngine), manifest.ReEngineInstallDir);
        Assert.Equal("Studio/Suite/ForwardApp", manifest.UmbrellaSubpath);
        Assert.True(manifest.MigrateLegacy);
    }

    [Fact]
    public void StandardInstallerHost_VisibleTestDefault_InstallsAndHonorsHeadless()
    {
        string payloadZip = BuildPayloadZip("HostVis.exe");
        var desc = new InstallerAppDescriptor("HostVis", "Host Visible", "Studio", "0.4.1", AllowAutoStart: false);

        string s1 = Path.Combine(_tempRoot, "HostVis");
        var sw = Stopwatch.StartNew();
        InstallerHostRunResult visRes = StandardInstallerHost.RunWithResult(desc, new[]
        {
            "--test-mode", "--data-dir", Path.Combine(s1, "data"), "--log-dir", Path.Combine(s1, "logs"),
            "--sandbox-root", s1, "--install-dir", Path.Combine(s1, "app"), "--payload", payloadZip,
            "--profile", "full", "--suite-dir", Path.Combine(s1, "suite")
        });
        sw.Stop();
        Assert.Equal(0, visRes.ExitCode);
        Assert.True(File.Exists(visRes.ScreenshotPath));
        byte[] pngHeader = File.ReadAllBytes(visRes.ScreenshotPath!)[..4];
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, pngHeader);
        Assert.True(sw.ElapsedMilliseconds < 15000, $"Visible test took unexpectedly long: {sw.ElapsedMilliseconds}ms");

        string s2 = Path.Combine(_tempRoot, "HostHeadless");
        InstallerHostRunResult hlRes = StandardInstallerHost.RunWithResult(desc, new[]
        {
            "--test-mode", "--headless", "--data-dir", Path.Combine(s2, "data"), "--log-dir", Path.Combine(s2, "logs"),
            "--install-dir", Path.Combine(s2, "app"), "--payload", payloadZip
        });
        Assert.Equal(0, hlRes.ExitCode);
        Assert.Null(hlRes.ScreenshotPath);
        Assert.True(File.Exists(Path.Combine(s2, "app", "HostVis.exe")));
    }

    [Fact]
    public void VisibleTestWindowHelper_And_WebViewHost_SupportForegroundAndScreenshots()
    {
        string dataDir = Path.Combine(_tempRoot, "UiHost", "data");
        string logDir = Path.Combine(_tempRoot, "UiHost", "logs");
        var sandbox = new SandboxEnvironment("uiapp", new[] { "--test-mode", "--data-dir", dataDir, "--log-dir", logDir });
        using var form = new DisposableWebViewHostForm("http://127.0.0.1:5179", dataDir, sandbox, "UI App");
        Assert.True(form.IsVisibleTest);
        Assert.True(form.TopMost);
        Assert.Equal("UI App [VISIBLE TEST]", form.Text);
        form.Controls.Add(new Panel { Size = new Size(120, 40), Location = new Point(20, 20), BackColor = Color.SteelBlue });
        string? shot = form.CaptureVisibleTestScreenshot(logDir, "webview-visible-test.png");
        Assert.True(File.Exists(shot));
    }

    [Fact]
    public void AllV041TouchedAndModularizedFiles_StrictlyComplyWithLocCaps()
    {
        string devRoot = Path.Combine(ResolveFoundationRoot(), "Dev");
        var caps = new (string RelPath, int MaxLines)[]
        {
            ("src/Foundation.Core/SandboxEnvironment.cs", 400),
            ("src/foundation_core/cli.py", 400),
            ("src/Foundation.Installer/InstallerContracts.cs", 199),
            ("src/Foundation.Installer/InstallerSandboxContext.cs", 319),
            ("src/Foundation.Installer/InstallerEngine.Core.cs", 199),
            ("src/Foundation.Installer/InstallerEngine.InstallAndExtract.cs", 400),
            ("src/Foundation.Installer/InstallerEngine.VerifyAndUpgrade.cs", 400),
            ("src/Foundation.Installer/InstallerWizardForm.cs", 400),
            ("src/Foundation.Installer/InstallerWizardForm.LayoutAndTheme.cs", 400),
            ("src/Foundation.Installer/InstallerWizardForm.ExecutionAndLogs.cs", 400),
            ("src/Foundation.Installer/InstallerWizardForm.VisibleTest.cs", 199),
            ("src/Foundation.Installer/StandardInstallerHost.cs", 400),
            ("src/Foundation.UI/Desktop/VisibleTestWindowHelper.cs", 200),
            ("src/Foundation.UI/Desktop/DisposableWebViewHostForm.cs", 400),
            ("tools/sync_foundation_sdk.py", 260),
            ("tools/verify_sandbox_universal.py", 400),
            ("tools/build_release_universal.py", 400)
        };

        foreach ((string rel, int maxLines) in caps)
        {
            string full = Path.Combine(devRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            int lines = File.ReadAllLines(full).Length;
            Assert.True(lines <= maxLines, $"{rel} has {lines} lines (limit <= {maxLines})");
        }
    }

    private string BuildPayloadZip(string exeName)
    {
        string path = Path.Combine(_tempRoot, $"{Guid.NewGuid():N}_{exeName}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry(exeName);
        using var sw = new StreamWriter(entry.Open(), Encoding.UTF8);
        sw.Write("MZ_TEST_041");
        return path;
    }

    private static string ResolveFoundationRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Dev", "Foundation.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Foundation repository root.");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch (Exception ex) { _ = ex; /* Temp test directory cleanup is best-effort. */ }
    }
}
