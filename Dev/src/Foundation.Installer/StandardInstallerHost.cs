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

        InstallerCliOptions cli;
        try
        {
            cli = InstallerEngine.ParseCliArgs(args, descriptor);
        }
        catch (Exception parseEx)
        {
            Console.Error.WriteLine($"Setup argument error: {parseEx.Message}");
            return 2;
        }

        if (cli.ShowVersion)
        {
            Console.WriteLine(descriptor.Version);
            return 0;
        }

        if (cli.ShowHelp)
        {
            PrintBanner(descriptor);
            PrintUsage(descriptor);
            return 0;
        }

        var ctx = new InstallerSandboxContext(
            cliDataDir: cli.DataDir,
            cliConfigPath: cli.ConfigPath,
            cliLogDir: cli.LogDir,
            testMode: cli.TestMode,
            dryRun: cli.DryRun,
            headless: cli.Headless,
            envPrefix: descriptor.EnvPrefix,
            fallbackTestInstallDir: cli.InstallDir,
            visibleTest: cli.VisibleTest,
            cliSandboxRoot: cli.SandboxRoot);

        bool effectiveAutoStart = descriptor.AllowAutoStart && cli.AutoStart;
        if (cli.AutoStart && !descriptor.AllowAutoStart)
        {
            ctx.WriteLog("WARN", $"--autostart ignored for {descriptor.ProductName} because AllowAutoStart is false.");
            if (!cli.Silent)
            {
                Console.WriteLine($"Warning: --autostart ignored because {descriptor.DisplayName} disallows Startup registration.");
            }
        }

        if (ctx.VisibleTest && !cli.Silent && !ctx.Headless && OperatingSystem.IsWindows())
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            var showcaseEngine = new InstallerEngine(descriptor, ctx, resolvedAssembly);
            string showcaseDir = !string.IsNullOrWhiteSpace(cli.InstallDir)
                ? cli.InstallDir
                : (ctx.IsContainedMode && !string.IsNullOrWhiteSpace(ctx.DataDirectory)
                    ? Path.Combine(ctx.DataDirectory, "installed-app")
                    : showcaseEngine.ResolveDefaultInstallDirectory());
            using var showcaseWizard = new InstallerWizardForm(
                engine: showcaseEngine,
                ctx: ctx,
                initialInstallDir: showcaseDir,
                externalPayloadPath: cli.PayloadPath,
                isUninstallMode: cli.Uninstall,
                initialCreateShortcuts: !cli.NoShortcuts,
                initialAutoStart: effectiveAutoStart,
                initialLaunchAfter: !cli.NoLaunch,
                initialPurgeData: cli.PurgeData,
                showShowcaseAutoCloseMs: 3000,
                initialProfileId: cli.ProfileId,
                initialSecondaryDir: cli.SecondaryDir,
                secondaryFlagName: cli.SecondaryFlagName,
                initialMigrateLegacy: cli.MigrateLegacy);
            Application.Run(showcaseWizard);
            return showcaseWizard.ExitCode;
        }

        bool isNonInteractive = cli.Silent || ctx.Headless || ctx.TestMode;

        if (!isNonInteractive && !cli.DryRun && !cli.VerifyOnly && string.IsNullOrWhiteSpace(cli.InstallDir) && OperatingSystem.IsWindows())
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            var guiEngine = new InstallerEngine(descriptor, ctx, resolvedAssembly);
            using var wizard = new InstallerWizardForm(
                engine: guiEngine,
                ctx: ctx,
                initialInstallDir: cli.InstallDir,
                externalPayloadPath: cli.PayloadPath,
                isUninstallMode: cli.Uninstall,
                initialCreateShortcuts: !cli.NoShortcuts,
                initialAutoStart: effectiveAutoStart,
                initialLaunchAfter: !cli.NoLaunch,
                initialPurgeData: cli.PurgeData,
                initialProfileId: cli.ProfileId,
                initialSecondaryDir: cli.SecondaryDir,
                secondaryFlagName: cli.SecondaryFlagName,
                initialMigrateLegacy: cli.MigrateLegacy);
            Application.Run(wizard);
            return wizard.ExitCode;
        }

        return RunCliFlowAsync(
            descriptor,
            ctx,
            resolvedAssembly,
            cli.InstallDir,
            cli.PayloadPath,
            cli.Uninstall,
            cli.PurgeData,
            cli.VerifyOnly,
            cli.NoShortcuts,
            effectiveAutoStart,
            cli.LaunchAfter,
            cli.NoLaunch,
            cli.Silent,
            cli.DryRun).GetAwaiter().GetResult();
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

    private static void PrintBanner(InstallerAppDescriptor descriptor)
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine($"  {descriptor.DisplayName} Setup & Self-Contained Installer (v{descriptor.Version})");
        Console.WriteLine("=================================================================");
        Console.WriteLine();
    }

    private static void PrintUsage(InstallerAppDescriptor descriptor)
    {
        Console.WriteLine($"Usage: {descriptor.ProductName}-Setup.exe [--install-dir <dir>] [--profile <lightweight|full>] [--reengine-dir <dir>] [--suite-dir <dir>] [--sandbox-root <dir>] [--migrate-legacy] [--silent] [--verify] [--uninstall] [--purge-data] [--test-mode] [--visible-test] [--data-dir <dir>] [--log-dir <dir>] [--config <path>]");
    }
}
