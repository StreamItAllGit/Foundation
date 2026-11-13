// @module: Foundation.Installer.StandardInstallerHost
// @purpose: Turnkey 4-Pillar Single-EXE setup entry point supporting dark GUI wizard, folder browse, --silent, --verify, --uninstall, --test-mode, and --dry-run CLI flags.
// @depends-on: System, System.Diagnostics, System.IO, System.Reflection, System.Runtime.InteropServices, System.Threading.Tasks, System.Windows.Forms
// @exports: StandardInstallerHost

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Foundation.Installer;

public static class StandardInstallerHost
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    public static int Run(InstallerAppDescriptor descriptor, string[] args, Assembly? payloadAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        args ??= Array.Empty<string>();
        Assembly resolvedAssembly = payloadAssembly ?? Assembly.GetCallingAssembly();

        if (args.Length > 0 && OperatingSystem.IsWindows())
        {
            try
            {
                if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
                {
                    AttachConsole(AttachParentProcess);
                }
            }
            catch { }
        }

        string? installDir = null;
        string? dataDir = null;
        string? configPath = null;
        string? logDir = null;
        string? payloadPath = null;
        bool uninstall = false;
        bool purgeData = false;
        bool verifyOnly = false;
        bool noShortcuts = false;
        bool autoStart = false;
        bool launchAfter = false;
        bool noLaunch = false;
        bool silent = false;
        bool testMode = false;
        bool dryRun = false;
        bool headless = false;
        bool visibleTest = false;
        bool showHelp = false;
        bool showVersion = false;

        string? selfExeName = Path.GetFileName(Environment.ProcessPath);
        if (string.Equals(selfExeName, descriptor.UninstallerExeName, StringComparison.OrdinalIgnoreCase))
        {
            uninstall = true;
        }

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].Trim();
                switch (arg.ToLowerInvariant())
                {
                    case "--install-dir":
                    case "--target-dir":
                        installDir = RequireNextValue(args, ref i, arg);
                        break;
                    case "--data-dir":
                        dataDir = RequireNextValue(args, ref i, arg);
                        break;
                    case "--config":
                        configPath = RequireNextValue(args, ref i, arg);
                        break;
                    case "--log-dir":
                        logDir = RequireNextValue(args, ref i, arg);
                        break;
                    case "--payload":
                        payloadPath = RequireNextValue(args, ref i, arg);
                        break;
                    case "--uninstall":
                        uninstall = true;
                        break;
                    case "--purge-data":
                        purgeData = true;
                        break;
                    case "--verify":
                        verifyOnly = true;
                        break;
                    case "--no-shortcuts":
                        noShortcuts = true;
                        break;
                    case "--autostart":
                        autoStart = true;
                        break;
                    case "--launch":
                        launchAfter = true;
                        break;
                    case "--no-launch":
                    case "--no-restart":
                        noLaunch = true;
                        break;
                    case "--silent":
                    case "--quiet":
                        silent = true;
                        break;
                    case "--test-mode":
                        testMode = true;
                        break;
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--headless":
                        headless = true;
                        break;
                    case "--visible-test":
                        visibleTest = true;
                        break;
                    case "--version":
                    case "-v":
                        showVersion = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                    case "help":
                        showHelp = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option '{arg}'. Pass --help for usage.");
                }
            }
        }
        catch (Exception parseEx)
        {
            Console.Error.WriteLine($"Setup argument error: {parseEx.Message}");
            return 2;
        }

        if (showVersion)
        {
            Console.WriteLine(descriptor.Version);
            return 0;
        }

        if (showHelp)
        {
            PrintBanner(descriptor);
            PrintUsage(descriptor);
            return 0;
        }

        var ctx = new InstallerSandboxContext(
            cliDataDir: dataDir,
            cliConfigPath: configPath,
            cliLogDir: logDir,
            testMode: testMode,
            dryRun: dryRun,
            headless: headless,
            envPrefix: descriptor.EnvPrefix,
            fallbackTestInstallDir: installDir,
            visibleTest: visibleTest);

        bool effectiveAutoStart = descriptor.AllowAutoStart && autoStart;
        if (autoStart && !descriptor.AllowAutoStart)
        {
            ctx.WriteLog("WARN", $"--autostart ignored for {descriptor.ProductName} because AllowAutoStart is false.");
            if (!silent)
            {
                Console.WriteLine($"Warning: --autostart ignored because {descriptor.DisplayName} disallows Startup registration.");
            }
        }

        if (ctx.VisibleTest && !silent && !ctx.Headless && OperatingSystem.IsWindows())
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            var showcaseEngine = new InstallerEngine(descriptor, ctx, resolvedAssembly);
            string showcaseDir = !string.IsNullOrWhiteSpace(installDir)
                ? installDir
                : (ctx.IsContainedMode && !string.IsNullOrWhiteSpace(ctx.DataDirectory)
                    ? Path.Combine(ctx.DataDirectory, "installed-app")
                    : showcaseEngine.ResolveDefaultInstallDirectory());
            using var showcaseWizard = new InstallerWizardForm(
                engine: showcaseEngine,
                ctx: ctx,
                initialInstallDir: showcaseDir,
                externalPayloadPath: payloadPath,
                isUninstallMode: uninstall,
                initialCreateShortcuts: !noShortcuts,
                initialAutoStart: effectiveAutoStart,
                initialLaunchAfter: !noLaunch,
                initialPurgeData: purgeData,
                showShowcaseAutoCloseMs: 3000);
            Application.Run(showcaseWizard);
            return showcaseWizard.ExitCode;
        }

        bool isNonInteractive = silent || ctx.Headless || ctx.TestMode;

        if (!isNonInteractive && !dryRun && !verifyOnly && string.IsNullOrWhiteSpace(installDir) && OperatingSystem.IsWindows())
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            var guiEngine = new InstallerEngine(descriptor, ctx, resolvedAssembly);
            using var wizard = new InstallerWizardForm(
                engine: guiEngine,
                ctx: ctx,
                initialInstallDir: installDir,
                externalPayloadPath: payloadPath,
                isUninstallMode: uninstall,
                initialCreateShortcuts: !noShortcuts,
                initialAutoStart: effectiveAutoStart,
                initialLaunchAfter: !noLaunch,
                initialPurgeData: purgeData);
            Application.Run(wizard);
            return wizard.ExitCode;
        }

        return RunCliFlowAsync(
            descriptor,
            ctx,
            resolvedAssembly,
            installDir,
            payloadPath,
            uninstall,
            purgeData,
            verifyOnly,
            noShortcuts,
            effectiveAutoStart,
            launchAfter,
            noLaunch,
            silent,
            dryRun).GetAwaiter().GetResult();
    }

    private static async Task<int> RunCliFlowAsync(
        InstallerAppDescriptor descriptor,
        InstallerSandboxContext ctx,
        Assembly payloadAssembly,
        string? installDir,
        string? payloadPath,
        bool uninstall,
        bool purgeData,
        bool verifyOnly,
        bool noShortcuts,
        bool autoStart,
        bool launchAfter,
        bool noLaunch,
        bool silent,
        bool dryRun)
    {
        if (!silent)
        {
            PrintBanner(descriptor);
        }

        try
        {
            var engine = new InstallerEngine(descriptor, ctx, payloadAssembly);

            if (verifyOnly)
            {
                var verify = await engine.VerifyPayloadAsync(payloadPath);
                Console.WriteLine(verify.Message);
                return verify.Valid ? 0 : 1;
            }

            if (uninstall)
            {
                var unRes = await engine.UninstallAsync(installDir, purgeData);
                Console.WriteLine(unRes.Message);
                return unRes.Success ? 0 : 1;
            }

            var res = await engine.InstallAsync(
                explicitInstallDir: installDir,
                createShortcuts: !noShortcuts,
                registerAutoStart: autoStart,
                externalPayloadPath: payloadPath);

            Console.WriteLine(res.Message);
            if (!res.Success)
            {
                return 1;
            }

            if ((launchAfter || res.WasRunningBeforeInstall) && !noLaunch && !dryRun && !ctx.IsContainedMode)
            {
                string installedExe = engine.ResolveInstalledExecutablePath(res.InstallDirectory);
                if (File.Exists(installedExe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = installedExe,
                        WorkingDirectory = res.InstallDirectory,
                        UseShellExecute = true
                    });
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            ctx.WriteLog("ERROR", $"Installer failed: {ex.Message}");
            Console.Error.WriteLine($"Setup error: {ex.Message}");
            return 1;
        }
    }

    private static string RequireNextValue(string[] args, ref int index, string flag)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"Option '{flag}' requires a value.");
        }
        index++;
        return args[index].Trim();
    }

    private static void PrintBanner(InstallerAppDescriptor descriptor)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine($"  {descriptor.DisplayName} Setup & Self-Contained Installer (v{descriptor.Version})");
        Console.WriteLine("=================================================================");
        Console.WriteLine();
    }

    private static void PrintUsage(InstallerAppDescriptor descriptor)
    {
        Console.WriteLine($"Usage: {descriptor.ProductName}-Setup.exe [--install-dir <dir>] [--silent] [--verify] [--uninstall] [--purge-data] [--test-mode] [--visible-test] [--data-dir <dir>] [--log-dir <dir>] [--config <path>]");
    }
}
