// @module: Foundation.Tests.ReleaseFallbackAndUpdater
// @purpose: Verifies Online Setup GitHub-first + R2-fallback resolution, per-project PIN header injection, and ReleaseUpdaterClient update checks.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Net, System.Net.Http, System.Security.Cryptography, System.Text, System.Threading, System.Threading.Tasks, Foundation.Core, Foundation.Installer, Xunit
// @exports: FoundationReleaseFallbackAndUpdaterTests

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;
using Foundation.Installer;
using Xunit;

namespace Foundation.Tests;

public sealed class FoundationReleaseFallbackAndUpdaterTests : IDisposable
{
    private readonly string _tempDir;

    public FoundationReleaseFallbackAndUpdaterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FoundationRelHub_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<(Uri Uri, string? BetaPin)> Requests { get; } = new();

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? pin = request.Headers.TryGetValues("X-Beta-Pin", out var vals) ? string.Join(",", vals) : null;
            Requests.Add((request.RequestUri!, pin));
            return Task.FromResult(_responder(request));
        }
    }

    private static (byte[] ZipBytes, string Sha256) CreateTestZip()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("DemoApp.exe");
            using var sw = new StreamWriter(entry.Open(), Encoding.UTF8);
            sw.Write("demo-binary");
        }
        byte[] bytes = ms.ToArray();
        string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return (bytes, sha);
    }

    [Fact]
    public async Task OnlineSetup_FallsBackFromGitHubToR2_WithBetaPinHeader()
    {
        (byte[] zipBytes, string sha256) = CreateTestZip();
        var handler = new StubHttpHandler(req =>
        {
            if (req.RequestUri!.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }
            if (req.RequestUri.Host.Equals("demo-beta.oddlotsoftworks.com", StringComparison.OrdinalIgnoreCase))
            {
                if (!req.Headers.TryGetValues("X-Beta-Pin", out var vals) || string.Join(",", vals) != "PIN12345")
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);

                var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) };
                resp.Content.Headers.ContentLength = zipBytes.Length;
                return resp;
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var desc = new InstallerAppDescriptor("DemoApp", "Demo App", "Oddlot", "0.2.0")
        {
            InstallerDeliveryKind = InstallerDeliveryKinds.OnlineSetup,
            PayloadDownloadUrl = "https://github.com/OddlotSoftworks/DemoApp/releases/download/v0.2.0",
            PayloadFallbackUrls = new[] { "https://demo-beta.oddlotsoftworks.com/dl/{pin}/v0.2.0" },
            BetaPin = "PIN12345",
            PayloadAssets = new[] { new InstallerPayloadAsset("*", "DemoApp-v0.2.0-Portable.zip", zipBytes.Length, sha256) }
        };

        var ctx = new InstallerRuntimeContext(cliDataDir: Path.Combine(_tempDir, "data"), envPrefix: "DEMOAPP");
        var engine = new InstallerEngine(desc, ctx) { CustomHttpHandler = handler };
        string installDir = Path.Combine(_tempDir, "install");

        InstallerOperationResult result = await engine.InstallAsync(new InstallerInstallOptions(ExplicitInstallDir: installDir));
        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(installDir, "DemoApp.exe")));
        Assert.Contains(handler.Requests, r => r.Uri.Host == "github.com" && r.BetaPin == null);
        Assert.Contains(handler.Requests, r => r.Uri.Host == "demo-beta.oddlotsoftworks.com" && r.BetaPin == "PIN12345");
    }

    [Fact]
    public async Task ReleaseUpdaterClient_ResolvesLocalGitignoredPin_And_FallsBackToR2()
    {
        (byte[] zipBytes, string sha256) = CreateTestZip();
        string appDir = Path.Combine(_tempDir, "app");
        Directory.CreateDirectory(appDir);

        File.WriteAllText(Path.Combine(appDir, ".beta.manifest.local.json"), """
        { "betaPin": "SECRET999" }
        """);
        File.WriteAllText(Path.Combine(appDir, "update.manifest.json"), """
        {
          "schemaVersion": 1,
          "productName": "DemoApp",
          "projectSlug": "demoapp",
          "studio": "oddlot",
          "visibility": "github_public",
          "channel": "prerelease",
          "currentVersion": "0.1.0",
          "primaryManifestUrl": "https://api.github.com/repos/OddlotSoftworks/DemoApp/releases",
          "fallbackManifestUrls": ["https://demoapp-beta.oddlotsoftworks.com/dl/{pin}/latest.json"]
        }
        """);

        ReleaseUpdateManifest? loaded = ReleaseUpdaterClient.LoadManifest(appDir);
        Assert.NotNull(loaded);
        Assert.Equal("SECRET999", loaded!.BetaPin);

        var handler = new StubHttpHandler(req =>
        {
            if (req.RequestUri!.Host == "api.github.com")
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            if (req.RequestUri.AbsolutePath.EndsWith("/latest.json", StringComparison.OrdinalIgnoreCase))
            {
                string json = $$"""
                {
                  "version": "0.2.0-beta.1",
                  "channel": "prerelease",
                  "releaseNotes": "Beta notes",
                  "assets": [
                    {
                      "fileName": "DemoApp-v0.2.0-beta.1-Portable.zip",
                      "sizeBytes": {{zipBytes.Length}},
                      "sha256": "{{sha256}}",
                      "downloadUrl": "https://demoapp-beta.oddlotsoftworks.com/dl/SECRET999/v0.2.0-beta.1/DemoApp-v0.2.0-beta.1-Portable.zip"
                    }
                  ]
                }
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }

            var bin = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) };
            bin.Content.Headers.ContentLength = zipBytes.Length;
            return bin;
        });

        ReleaseUpdateCheckResult check = await ReleaseUpdaterClient.CheckForUpdateAsync(loaded, customHandler: handler);
        Assert.True(check.UpdateAvailable);
        Assert.Equal("r2", check.SourceKind);
        Assert.Equal("0.2.0-beta.1", check.LatestVersion);

        string outZip = Path.Combine(_tempDir, "downloaded.zip");
        SafeTransferResult dl = await ReleaseUpdaterClient.DownloadVerifiedUpdateAsync(
            check, outZip, loaded.BetaPin, customHandler: handler);
        Assert.Equal(sha256, dl.Sha256Hex);
        Assert.True(File.Exists(outZip));
    }
}
