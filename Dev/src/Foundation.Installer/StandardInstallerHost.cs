// @module: Foundation.Installer.StandardInstallerHost
// @purpose: Turnkey 4-Pillar Single-EXE setup entry point supporting dark GUI wizard and --silent, --verify, --uninstall, and --dry-run CLI flags with ExitCode=3 containment.
// @depends-on: System, System.Diagnostics, System.IO, System.Reflection, System.Runtime.InteropServices, System.Threading.Tasks, System.Windows.Forms, Foundation.Core
// @exports: StandardInstallerHost

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Foundation.Core;

namespace Foundation.Installer;

public static class StandardInstallerHost
{
    private const int AttachParentProcess = -1;
    private static bool _winFormsInitialized;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    private static void InitializeWinFormsHost()
    {
        if (_winFormsInitialized)
            return;

        _winFormsInitialized = true;
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        }
        catch (InvalidOperationException ex)
        {
            _ = ex; // In-process hosts/test runners that already created handles cannot re-run SetCompatibleTextRenderingDefault.
        }
    }

    public static int Run(InstallerAppDescriptor descriptor, string[] args, Assembly? payloadAssembly = null) =>
        RunWithResult(descriptor, args, payloadAssembly ?? Assembly.GetCallingAssembly()).ExitCode;

    public static InstallerHostRunResult RunWithResult(InstallerAppDescriptor descriptor, string[] args, Assembly? payloadAssembly = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        args ??= Array.Empty<string>();
        Assembly resolvedAssembly = payloadAssembly ?? Assembly.GetCallingAssembly();
        InstallerMarkupManifest? markup = InstallerManifestStore.TryLoadFromDirectoryOrAssembly(
            AppContext.BaseDirectory,
            resolvedAssembly);
        InstallerAppDescriptor effectiveDescriptor = markup is not null
            ? InstallerManifestStore.ApplyToDescriptor(descriptor, markup)
            : descriptor;

        if (args.Length > 0 && OperatingSystem.IsWindows())
        {
            try
            {
                if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
                {
                    AttachConsole(AttachParentProcess);
                }
            }
            catch (Exception ex)
            {
                _ = ex; // Console attachment is optional under non-console test runners or GUI parents.
            }
        }

        InstallerCliOptions cli;
        try
        {
            cli = InstallerEngine.ParseCliArgs(args, effectiveDescriptor);
        }
        catch (Exception parseEx)
        {
            Console.Error.WriteLine($"Setup argument error: {parseEx.Message}");
            return new InstallerHostRunResult(2);
        }

        if (cli.ShowVersion)
        {
            Console.WriteLine(effectiveDescriptor.Version);
            return new InstallerHostRunResult(0);
        }

        if (cli.ShowHelp)
        {
            PrintBanner(effectiveDescriptor);
            PrintUsage(effectiveDescriptor);
            return new InstallerHostRunResult(0);
        }

        InstallerRuntimeContext? ctx = null;
        try
        {
            ctx = new InstallerRuntimeContext(
                cliDataDir: cli.DataDir,
                cliConfigPath: cli.ConfigPath,
                cliLogDir: cli.LogDir,
                dryRun: cli.DryRun,
                envPrefix: effectiveDescriptor.EnvPrefix);

            bool effectiveAutoStart = effectiveDescriptor.AllowAutoStart && cli.AutoStart;
            if (cli.AutoStart && !effectiveDescriptor.AllowAutoStart)
            {
                ctx.WriteLog("WARN", $"--autostart ignored for {effectiveDescriptor.ProductName} because AllowAutoStart is false.");
                if (!cli.Silent)
                {
                    Console.WriteLine($"Warning: --autostart ignored because {effectiveDescriptor.DisplayName} disallows Startup registration.");
                }
            }

            bool explicitMigrate = cli.MigrateLegacy || (ctx.ConfigOverride?.MigrateLegacy ?? false);
            bool showLegacy = effectiveDescriptor.AllowLegacyMigration || explicitMigrate;
            bool isNonInteractive = cli.Silent || ctx.IsIsolatedTestProcess;

            if (!isNonInteractive && !cli.DryRun && !cli.VerifyOnly && string.IsNullOrWhiteSpace(cli.InstallDir) && OperatingSystem.IsWindows())
            {
                InitializeWinFormsHost();
                var guiEngine = new InstallerEngine(effectiveDescriptor, ctx, resolvedAssembly);
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
                    initialMigrateLegacy: explicitMigrate,
                    showLegacyMigration: showLegacy,
                    payloadAssembly: resolvedAssembly,
                    offlineBundlePath: cli.OfflineBundlePath);
                Application.Run(wizard);
                return new InstallerHostRunResult(wizard.ExitCode, ctx.LogDirectory);
            }

            return SynchronizationContext.Current != null
                ? Task.Run(() => RunCliFlowAsync(effectiveDescriptor, ctx, resolvedAssembly, cli, effectiveAutoStart, explicitMigrate)).GetAwaiter().GetResult()
                : RunCliFlowAsync(effectiveDescriptor, ctx, resolvedAssembly, cli, effectiveAutoStart, explicitMigrate).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            int exitCode = SandboxEnvironmentSeam.IsContainmentViolation(ex)
                ? SandboxEnvironmentSeam.ContainmentExitCode
                : 1;
            ctx?.WriteLog("ERROR", $"Installer host failed (ExitCode={exitCode}): {ex.Message}");
            Console.Error.WriteLine($"Setup error: {ex.Message}");
            return new InstallerHostRunResult(exitCode, ctx?.LogDirectory);
        }
    }

    private static async Task<InstallerHostRunResult> RunCliFlowAsync(
        InstallerAppDescriptor descriptor,
        InstallerRuntimeContext ctx,
        Assembly payloadAssembly,
        InstallerCliOptions cli,
        bool autoStart,
        bool migrateLegacy)
    {
        if (!cli.Silent)
        {
            PrintBanner(descriptor);
        }

        try
        {
            var engine = new InstallerEngine(descriptor, ctx, payloadAssembly);

            if (cli.VerifyOnly)
            {
                var verify = await engine.VerifyPayloadAsync(cli.PayloadPath, cli.OfflineBundlePath, cli.ProfileId);
                Console.WriteLine(verify.Message);
                return new InstallerHostRunResult(verify.Valid ? 0 : 1, ctx.LogDirectory);
            }

            if (cli.Uninstall)
            {
                var unRes = await engine.UninstallAsync(cli.InstallDir, cli.PurgeData);
                Console.WriteLine(unRes.Message);
                return new InstallerHostRunResult(unRes.Success ? 0 : 1, ctx.LogDirectory, unRes);
            }

            var res = await engine.InstallAsync(new InstallerInstallOptions(
                ExplicitInstallDir: cli.InstallDir,
                CreateShortcuts: !cli.NoShortcuts,
                RegisterAutoStart: autoStart,
                ExternalPayloadPath: cli.PayloadPath,
                InstallProfile: cli.ProfileId,
                SecondaryInstallDir: cli.SecondaryDir,
                SecondaryFlagName: cli.SecondaryFlagName,
                MigrateLegacy: migrateLegacy)
            {
                OfflineBundlePath = cli.OfflineBundlePath
            });

            Console.WriteLine(res.Message);
            if (!res.Success)
            {
                return new InstallerHostRunResult(1, ctx.LogDirectory, res);
            }

            if ((cli.LaunchAfter || res.WasRunningBeforeInstall) && !cli.NoLaunch && !cli.DryRun && !ctx.ShouldSuppressHostMutations)
            {
                string installedExe = engine.ResolveInstalledExecutablePath(res.InstallDirectory);
                if (ctx.IsSandboxActive)
                    installedExe = SandboxEnvironmentSeam.AssertSandboxPathIfActive(installedExe, ctx.Sandbox);
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

            return new InstallerHostRunResult(0, ctx.LogDirectory, res);
        }
        catch (Exception ex)
        {
            int exitCode = SandboxEnvironmentSeam.IsContainmentViolation(ex)
                ? SandboxEnvironmentSeam.ContainmentExitCode
                : 1;
            ctx.WriteLog("ERROR", $"Installer failed (ExitCode={exitCode}): {ex.Message}");
            Console.Error.WriteLine($"Setup error: {ex.Message}");
            return new InstallerHostRunResult(exitCode, ctx.LogDirectory);
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
        Console.WriteLine($"Usage: {descriptor.ProductName}-Setup.exe [--install-dir <dir>] [--profile <lightweight|full|custom>] [--offline-bundle <zipOrDir>] [--reengine-dir <dir>] [--suite-dir <dir>] [--migrate-legacy] [--silent] [--verify] [--uninstall] [--purge-data] [--dry-run] [--data-dir <dir>] [--log-dir <dir>] [--config <path>]");
    }
}
