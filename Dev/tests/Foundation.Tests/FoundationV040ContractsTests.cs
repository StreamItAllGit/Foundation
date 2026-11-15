// @module: Foundation.Tests.V040Contracts
// @purpose: Acceptance tests for Foundation v0.4.0 contracts (Studio/Suite install root, sibling Sandbox containment, Installer CLI, SafeNetworkTransfer, RootLauncherHost, ConsoleSystemCatalog, and AOT/Recomp contracts).
// @depends-on: System, System.IO, System.Net, System.Net.Http, System.Text, System.Text.RegularExpressions, System.Threading, System.Threading.Tasks, Xunit, Foundation.Core, Foundation.Installer, Foundation.Launcher, Foundation.Retro
// @exports: FoundationV040ContractsTests

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;
using Foundation.Installer;
using Foundation.Launcher;
using Foundation.Retro;
using Xunit;

namespace Foundation.Tests;

public sealed class FoundationV040ContractsTests : IDisposable
{
    private readonly string _tempDir;

    public FoundationV040ContractsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FoundationV040Tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void InstallerAppDescriptor_ResolvesLocalAppDataProgramsRoot_WithZeroLegacyDriveFallback()
    {
        string localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");

        var standalone = new InstallerAppDescriptor("UniqueTestApp040", "Unique Test App", "StreamItAll", "0.4.0");
        Assert.Equal(Path.Combine(localPrograms, "StreamItAll", "UniqueTestApp040"), standalone.CanonicalDefaultInstallDir);
        Assert.DoesNotContain(@"D:\Programs", standalone.CanonicalDefaultInstallDir, StringComparison.OrdinalIgnoreCase);

        var suited = new InstallerAppDescriptor(
            "UniqueRecompApp040",
            "Unique Recomp App",
            "StreamItAll",
            "0.4.0",
            SuiteName: "reEngine");
        Assert.Equal(Path.Combine(localPrograms, "StreamItAll", "reEngine", "UniqueRecompApp040"), suited.CanonicalDefaultInstallDir);

        var umbrella = new InstallerAppDescriptor(
            "UniqueUmbrellaApp040",
            "Unique Umbrella App",
            "StreamItAll",
            "0.4.0",
            UmbrellaSubpath: @"CustomStudio\CustomSuite");
        Assert.Equal(Path.Combine(localPrograms, "CustomStudio", "CustomSuite", "UniqueUmbrellaApp040"), umbrella.CanonicalDefaultInstallDir);

        var uncontainedCtx = new InstallerSandboxContext(testMode: false, envPrefix: "UNIQUE_TEST_APP_040");
        var engine = new InstallerEngine(standalone, uncontainedCtx);
        string resolvedDefault = engine.ResolveDefaultInstallDirectory();
        Assert.StartsWith(localPrograms, resolvedDefault, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"D:\Programs", resolvedDefault, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SandboxEnvironment_And_InstallerEngine_EnforceFailClosedSiblingContainment()
    {
        string sandboxRoot = Path.Combine(_tempDir, "Sandbox");
        string sandboxData = Path.Combine(sandboxRoot, "data");
        string sandboxInstalled = Path.Combine(sandboxRoot, "installed");
        Directory.CreateDirectory(sandboxData);

        Assert.Equal(sandboxRoot, SandboxEnvironment.ResolveSiblingSandboxRoot(sandboxData));
        Assert.Null(SandboxEnvironment.ResolveSiblingSandboxRoot(null));
        Assert.Null(SandboxEnvironment.ResolveSiblingSandboxRoot(Path.Combine(_tempDir, "HostOnly", "data")));

        var descriptor = new InstallerAppDescriptor("ContainmentApp", "Containment App", "StreamItAll", "0.4.0");
        var ctx = new InstallerSandboxContext(cliDataDir: sandboxData, testMode: true, envPrefix: "CONTAINMENT_APP");
        var engine = new InstallerEngine(descriptor, ctx);

        string normalized = engine.ValidateAndNormalizeInstallDirectory(sandboxInstalled);
        Assert.Equal(Path.GetFullPath(sandboxInstalled), normalized);

        string hostEscapePath = Path.Combine(Path.GetPathRoot(_tempDir) ?? @"C:\", "Program Files", "HostEscapeApp");
        Assert.Throws<InvalidOperationException>(() => engine.ValidateAndNormalizeInstallDirectory(hostEscapePath));
    }

    [Fact]
    public void InstallerEngine_ParseCliArgs_ParsesV040FlagsCorrectly()
    {
        var descriptor = new InstallerAppDescriptor("CliTestApp", "CLI Test App", "StreamItAll", "0.4.0");
        string[] args =
        {
            "--profile", "Full",
            "--reengine-dir", @"C:\Sandbox\reEngine",
            "--sandbox-root", @"C:\Sandbox",
            "--migrate-legacy",
            "--test-mode"
        };

        InstallerCliOptions opts = InstallerEngine.ParseCliArgs(args, descriptor);
        Assert.Equal("Full", opts.ProfileId);
        Assert.Equal(@"C:\Sandbox\reEngine", opts.ReEngineDir);
        Assert.Null(opts.SuiteDir);
        Assert.Equal(@"C:\Sandbox\reEngine", opts.SecondaryDir);
        Assert.Equal("--reengine-dir", opts.SecondaryFlagName);
        Assert.Equal(@"C:\Sandbox", opts.SandboxRoot);
        Assert.True(opts.MigrateLegacy);
        Assert.True(opts.TestMode);

        InstallerCliOptions suiteOpts = InstallerEngine.ParseCliArgs(
            new[] { "--suite-dir", @"C:\Sandbox\Suite" },
            descriptor);
        Assert.Equal(@"C:\Sandbox\Suite", suiteOpts.SuiteDir);
        Assert.Equal(@"C:\Sandbox\Suite", suiteOpts.SecondaryDir);
        Assert.Equal("--suite-dir", suiteOpts.SecondaryFlagName);
    }

    [Fact]
    public async Task SafeNetworkTransfer_SanitizesJobId_Recovers416_DoesNotRetry404_And_EnforcesResponseHeadersTimeout()
    {
        // 1. JobId sanitization (colons/slashes must not create NTFS ADS or invalid paths)
        string dest1 = Path.Combine(_tempDir, "job-sanitize.bin");
        var handler1 = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("hello-world"))
        });

        SafeTransferResult res1 = await SafeNetworkTransfer.DownloadFileAsync(
            new Uri("https://example.invalid/file.bin"),
            dest1,
            new SafeTransferOptions { JobId = "bad:job/id\\name", MinFreeSpaceReserveBytes = 0 },
            customHandler: handler1);
        Assert.True(File.Exists(res1.FinalFilePath));
        Assert.Equal("hello-world", File.ReadAllText(res1.FinalFilePath));

        // 2. HTTP 416 recovery: stale partial exists, server returns 416 on Range, then 200 on clean retry
        string dest2 = Path.Combine(_tempDir, "recover-416.bin");
        File.WriteAllText($"{dest2}.partial_job416", "stale-oversized-partial-bytes");
        int calls416 = 0;
        var handler416 = new StubHttpMessageHandler(req =>
        {
            calls416++;
            if (req.Headers.Range is not null)
            {
                return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("fresh-content"))
            };
        });

        SafeTransferResult res416 = await SafeNetworkTransfer.DownloadFileAsync(
            new Uri("https://example.invalid/recover.bin"),
            dest2,
            new SafeTransferOptions { JobId = "job416", MinFreeSpaceReserveBytes = 0 },
            customHandler: handler416);
        Assert.Equal(2, calls416);
        Assert.Equal("fresh-content", File.ReadAllText(res416.FinalFilePath));

        // 3. HTTP 404 non-retry: handler invoked exactly once even with MaxRetries = 3
        string dest3 = Path.Combine(_tempDir, "not-found.bin");
        int calls404 = 0;
        var handler404 = new StubHttpMessageHandler(_ =>
        {
            calls404++;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => SafeNetworkTransfer.DownloadFileAsync(
            new Uri("https://example.invalid/missing.bin"),
            dest3,
            new SafeTransferOptions { JobId = "job404", MaxRetries = 3, MinFreeSpaceReserveBytes = 0 },
            customHandler: handler404));
        Assert.Equal(1, calls404);

        // 4. ResponseHeadersTimeout aborts stalled headers
        string dest4 = Path.Combine(_tempDir, "header-timeout.bin");
        var stalledHandler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SafeNetworkTransfer.DownloadFileAsync(
            new Uri("https://example.invalid/slow.bin"),
            dest4,
            new SafeTransferOptions
            {
                JobId = "jobTimeout",
                MaxRetries = 0,
                ResponseHeadersTimeout = TimeSpan.FromMilliseconds(50),
                MinFreeSpaceReserveBytes = 0
            },
            customHandler: stalledHandler));
    }

    [Fact]
    public void RootLauncherHost_ShouldWaitForChild_And_NormalizeRelativePathArgs_BehaveCorrectly()
    {
        Assert.True(RootLauncherHost.ShouldWaitForChild(new[] { "--wait" }));
        Assert.True(RootLauncherHost.ShouldWaitForChild(new[] { "--open", "game.bin" }));
        Assert.True(RootLauncherHost.ShouldWaitForChild(new[] { "--check-updates" }));
        Assert.True(RootLauncherHost.ShouldWaitForChild(new[] { "--register-assoc" }));
        Assert.True(RootLauncherHost.ShouldWaitForChild(new[] { "--visible-test" }));
        Assert.True(RootLauncherHost.ShouldWaitForChild(new[] { "--test-mode" }));
        Assert.False(RootLauncherHost.ShouldWaitForChild(Array.Empty<string>()));
        Assert.False(RootLauncherHost.ShouldWaitForChild(new[] { "--tray" }));

        string callerCwd = Path.Combine(_tempDir, "caller");
        Directory.CreateDirectory(callerCwd);
        string[] normalized = RootLauncherHost.NormalizeRelativePathArgs(
            new[] { "--open", "roms/test.bin", "--input-script=scripts/run.json", "--tray", @".\save.dat" },
            callerCwd);

        Assert.Equal("--open", normalized[0]);
        Assert.Equal(Path.GetFullPath("roms/test.bin", callerCwd), normalized[1]);
        Assert.Equal($"--input-script={Path.GetFullPath("scripts/run.json", callerCwd)}", normalized[2]);
        Assert.Equal("--tray", normalized[3]);
        Assert.Equal(Path.GetFullPath(@".\save.dat", callerCwd), normalized[4]);
    }

    [Fact]
    public void ConsoleSystemCatalog_HasZeroFabricatedSequentialHexStrings_And_TracksVerifiedFlag()
    {
        List<RetroPlatformCatalogEntry> platforms = ConsoleSystemCatalog.BuildDefaultPlatforms();
        Assert.NotEmpty(platforms);

        var sequentialOrSyntheticPattern = new Regex(
            @"(01234567|12345678|abcdef01|fedcba98|deadbeef|cafebabe|0000000000000000|ffffffffffffffff)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        foreach (RetroPlatformCatalogEntry platform in platforms)
        {
            foreach (RetroBiosRequirement bios in platform.RequiredBiosFiles)
            {
                Assert.False(
                    sequentialOrSyntheticPattern.IsMatch(bios.ExpectedMd5),
                    $"Synthetic MD5 detected for {platform.PlatformId}/{bios.FileName}: {bios.ExpectedMd5}");
                Assert.False(
                    sequentialOrSyntheticPattern.IsMatch(bios.ExpectedSha1),
                    $"Synthetic SHA1 detected for {platform.PlatformId}/{bios.FileName}: {bios.ExpectedSha1}");

                if (string.IsNullOrEmpty(bios.ExpectedMd5) && string.IsNullOrEmpty(bios.ExpectedSha1))
                {
                    Assert.False(bios.IsVerifiedReferenceHash);
                }
            }
        }
    }

    [Fact]
    public void AotCacheLayoutHelper_And_RecompTierContracts_ResolveDeterministicCachePaths()
    {
        Assert.Equal(0, (int)RecompExecutionTierMode.AotPrimary);
        Assert.True(RecompRenderBackendIds.IsSupported(RecompRenderBackendIds.Software));
        Assert.True(RecompRenderBackendIds.IsSupported("OPENGL"));
        Assert.False(RecompRenderBackendIds.IsSupported("vulkan-unknown"));

        string cacheRoot = AotCacheLayoutHelper.ResolveModuleCacheRoot(_tempDir, "KingsFieldRecomp", "0.1.6");
        Assert.Equal(Path.Combine(Path.GetFullPath(_tempDir), "cache", "KingsFieldRecomp", "0.1.6"), cacheRoot);
        Assert.Equal(
            Path.Combine(cacheRoot, "KingsFieldRecomp.Generated.dll"),
            AotCacheLayoutHelper.ResolveGeneratedAssemblyPath(_tempDir, "KingsFieldRecomp", "0.1.6"));
        Assert.Equal(
            Path.Combine(cacheRoot, AotCacheLayoutHelper.BringupProfileFileName),
            AotCacheLayoutHelper.ResolveBringupProfilePath(_tempDir, "KingsFieldRecomp", "0.1.6"));

        Assert.Throws<ArgumentException>(() => AotCacheLayoutHelper.ResolveModuleCacheRoot(_tempDir, "../escape", "0.1.6"));
        Assert.Throws<ArgumentException>(() => AotCacheLayoutHelper.ResolveModuleCacheRoot(_tempDir, "KingsField", "1.0:bad"));

        var snapshot = new RecompRuntimeTelemetrySnapshot(
            RecompExecutionTierMode.AotPrimary,
            RecompRenderBackendIds.Software,
            FrameNumber: 120,
            FrameTimeMs: 16.6,
            CurrentFps: 60.0,
            P99FrameMs: 18.2,
            AotCacheHits: 120,
            JitFallbackCount: 0,
            IsAotCacheWarm: true,
            CapturedAtUtc: DateTimeOffset.UtcNow);
        Assert.Equal(RecompExecutionTierMode.AotPrimary, snapshot.ExecutionTier);
        Assert.True(snapshot.IsAotCacheWarm);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> syncHandler)
            : this((req, _) => Task.FromResult(syncHandler(req)))
        {
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> asyncHandler)
        {
            _handler = asyncHandler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
