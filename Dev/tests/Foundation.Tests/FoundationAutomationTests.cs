// @module: Foundation.Tests.Automation
// @purpose: Verifies Foundation.Automation loopback security (401/403), focus-free Win32 virtual pointer/keyboard/inspectTree, and offscreen PNG/RGBA snapshot round-trips.
// @depends-on: System, System.Drawing, System.IO, System.Net, System.Net.Http, System.Net.Http.Headers, System.Net.Sockets, System.Runtime.InteropServices, System.Text, System.Text.Json, System.Threading.Tasks, System.Windows.Forms, Foundation.Automation, Xunit
// @exports: FoundationAutomationTests

using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Foundation.Automation;
using Foundation.UI.Desktop;
using Xunit;

namespace Foundation.Tests;

public sealed class FoundationAutomationTests : IDisposable
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private readonly string _tempDir;

    public FoundationAutomationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FoundationAutoTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task LoopbackServer_EnforcesBearerToken401_And_HostOrigin403()
    {
        var opts = new AutomationServerOptions(0, "secret-token-v050", "auto-test", _tempDir);
        await using var server = new AutomationLoopbackServer(opts);
        server.Start();
        Assert.True(server.AutomationPort > 0);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.AutomationPort}") };

        HttpResponseMessage noAuth = await client.GetAsync("/v1/health");
        Assert.Equal(HttpStatusCode.Unauthorized, noAuth.StatusCode);

        using var badAuthReq = new HttpRequestMessage(HttpMethod.Get, "/v1/health");
        badAuthReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        HttpResponseMessage badAuth = await client.SendAsync(badAuthReq);
        Assert.Equal(HttpStatusCode.Unauthorized, badAuth.StatusCode);

        using var badOriginReq = new HttpRequestMessage(HttpMethod.Get, "/v1/health");
        badOriginReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "secret-token-v050");
        badOriginReq.Headers.Add("Origin", "https://attacker.example.com");
        HttpResponseMessage badOrigin = await client.SendAsync(badOriginReq);
        Assert.Equal(HttpStatusCode.Forbidden, badOrigin.StatusCode);

        using (var tcp = new TcpClient("127.0.0.1", server.AutomationPort))
        await using (NetworkStream ns = tcp.GetStream())
        {
            byte[] raw = Encoding.ASCII.GetBytes("GET /v1/health HTTP/1.1\r\nHost: evil.example.com\r\nAuthorization: Bearer secret-token-v050\r\n\r\n");
            await ns.WriteAsync(raw);
            byte[] respBuf = new byte[512];
            int read = await ns.ReadAsync(respBuf);
            string respText = Encoding.ASCII.GetString(respBuf, 0, read);
            Assert.StartsWith("HTTP/1.1 403 Forbidden", respText);
        }

        using var okReq = new HttpRequestMessage(HttpMethod.Get, "/v1/health");
        okReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "secret-token-v050");
        HttpResponseMessage okResp = await client.SendAsync(okReq);
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
        using var doc = JsonDocument.Parse(await okResp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("isPassed").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("data").TryGetProperty("generatedAtUtc", out _));
        Assert.Null(AutomationCliHelper.TryCreateFromArgs("auto-test", Array.Empty<string>(), null));
        using AutomationLoopbackServer? cliServer = AutomationCliHelper.TryCreateFromArgs("auto-test", new[] { "--automation-port", "0", "--automation-token", "tok" }, null, allowedSnapshotRoot: _tempDir);
        Assert.NotNull(cliServer);
    }

    [Fact]
    public async Task LoopbackServer_VirtualPointer_Keyboard_And_InspectTree_DoNotStealForegroundFocus()
    {
        int clicks = 0;
        TextBox? txt = null;
        CheckBox? chk = null;

        await RunWithStaFormAsync(() =>
        {
            var form = new Form { Width = 320, Height = 200, ShowInTaskbar = false, Name = "MainForm", Text = "TestApp" };
            txt = new TextBox { Name = "txtInput", Location = new Point(12, 12), Width = 180 };
            chk = new CheckBox { Name = "chkFlag", Text = "Agree", Location = new Point(12, 48), Checked = false };
            var btn = new Button { Name = "btnGo", Text = "Run", Location = new Point(12, 84), Size = new Size(90, 28) };
            btn.Click += (_, _) => Interlocked.Increment(ref clicks);
            form.Controls.AddRange(new Control[] { txt, chk, btn });
            return form;
        }, async form =>
        {
            IntPtr beforeFg = GetForegroundWindow();
            var opts = new AutomationServerOptions(0, "token-ui", "ui-app", _tempDir);
            await using var server = new AutomationLoopbackServer(opts, form);
            server.Start();

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.AutomationPort}") };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-ui");

            string treeJson = await client.GetStringAsync("/v1/tree");
            Assert.Contains("\"id\":\"btnGo\"", treeJson);
            Assert.Contains("\"id\":\"txtInput\"", treeJson);

            var clickBtn = new StringContent("{\"action\":\"click\",\"targetId\":\"btnGo\"}", Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/v1/pointer", clickBtn)).StatusCode);
            Assert.Equal(1, Volatile.Read(ref clicks));

            var clickChk = new StringContent("{\"action\":\"click\",\"targetId\":\"chkFlag\"}", Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/v1/pointer", clickChk)).StatusCode);
            Assert.True((bool)form.Invoke(() => chk!.Checked));

            foreach (string ptrBody in new[]
            {
                "{\"action\":\"move\",\"targetId\":\"btnGo\"}",
                "{\"action\":\"doubleClick\",\"targetId\":\"btnGo\"}",
                "{\"action\":\"drag\",\"targetId\":\"txtInput\",\"toX\":40,\"toY\":8}",
                "{\"action\":\"scroll\",\"targetId\":\"txtInput\",\"deltaY\":120}"
            })
            {
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/v1/pointer", new StringContent(ptrBody, Encoding.UTF8, "application/json"))).StatusCode);
            }
            Assert.Equal(2, Volatile.Read(ref clicks));

            var typeTxt = new StringContent("{\"action\":\"type\",\"text\":\"Hello Foundation\",\"targetId\":\"txtInput\"}", Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/v1/keyboard", typeTxt)).StatusCode);
            Assert.Equal("Hello Foundation", (string)form.Invoke(() => txt!.Text));

            var pressKey = new StringContent("{\"action\":\"press\",\"key\":\"End\",\"targetId\":\"txtInput\"}", Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/v1/keyboard", pressKey)).StatusCode);

            var hotkey = new StringContent("{\"action\":\"hotkey\",\"key\":\"A\",\"modifiers\":[\"ctrl\"],\"targetId\":\"txtInput\"}", Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/v1/keyboard", hotkey)).StatusCode);
            Assert.Equal("Hello Foundation".Length, (int)form.Invoke(() => txt!.SelectionLength));

            HttpResponseMessage badTarget = await client.PostAsync("/v1/pointer", new StringContent("{\"action\":\"click\",\"targetId\":\"missingControl\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, badTarget.StatusCode);
            using var badDoc = JsonDocument.Parse(await badTarget.Content.ReadAsStringAsync());
            Assert.False(badDoc.RootElement.GetProperty("isPassed").GetBoolean());

            Assert.Equal(beforeFg, GetForegroundWindow());
        });
    }

    [Fact]
    public async Task LoopbackServer_Snapshot_CapturesWinFormsPng_And_RgbaRenderSurfaceProvider()
    {
        string winShot = Path.Combine(_tempDir, "winform.png");
        await RunWithStaFormAsync(
            () => new DisposableWebViewHostForm("http://127.0.0.1:0", _tempDir, "WebViewPreview", null, 200, 120) { ShowInTaskbar = false },
            async form =>
            {
                await using var server = new AutomationLoopbackServer(new AutomationServerOptions(0, "snap-tok", "win", _tempDir), form);
                server.Start();
                using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.AutomationPort}") };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "snap-tok");
                string body = JsonSerializer.Serialize(new AutomationSnapshotRequest(winShot));
                HttpResponseMessage resp = await client.PostAsync("/v1/snapshot", new StringContent(body, Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

                string escaped = JsonSerializer.Serialize(new AutomationSnapshotRequest(Path.Combine(Path.GetTempPath(), "outside.png")));
                HttpResponseMessage escapedResp = await client.PostAsync("/v1/snapshot", new StringContent(escaped, Encoding.UTF8, "application/json"));
                Assert.NotEqual(HttpStatusCode.OK, escapedResp.StatusCode);
            });

        byte[] pngBytes = await File.ReadAllBytesAsync(winShot);
        Assert.True(pngBytes.Length > 24);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, pngBytes[..4]);

        var provider = new FakeRenderSurfaceProvider(4, 4, Color.FromArgb(255, 12, 180, 95));
        string glShot = Path.Combine(_tempDir, "gl.png");
        await using (var server = new AutomationLoopbackServer(new AutomationServerOptions(0, "gl-tok", "reengine", _tempDir), renderProvider: provider))
        {
            server.Start();
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.AutomationPort}") };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "gl-tok");
            string body = JsonSerializer.Serialize(new AutomationSnapshotRequest(glShot));
            HttpResponseMessage resp = await client.PostAsync("/v1/snapshot", new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        using var decoded = new Bitmap(glShot);
        Assert.Equal(4, decoded.Width);
        Assert.Equal(4, decoded.Height);
        Color px = decoded.GetPixel(0, 0);
        Assert.Equal(12, px.R);
        Assert.Equal(180, px.G);
        Assert.Equal(95, px.B);
    }

    private sealed class FakeRenderSurfaceProvider(int w, int h, Color c) : IRenderSurfaceSnapshotProvider
    {
        public bool TryCaptureRgba(out int width, out int height, out byte[] rgbaBytes)
        {
            width = w;
            height = h;
            rgbaBytes = new byte[w * h * 4];
            for (int i = 0; i < rgbaBytes.Length; i += 4)
            {
                rgbaBytes[i] = c.R;
                rgbaBytes[i + 1] = c.G;
                rgbaBytes[i + 2] = c.B;
                rgbaBytes[i + 3] = c.A;
            }
            return true;
        }
    }

    private static async Task RunWithStaFormAsync(Func<Form> factory, Func<Form, Task> body)
    {
        var ready = new TaskCompletionSource<Form>(TaskCreationOptions.RunContinuationsAsynchronously);
        Form? created = null;
        var uiThread = new Thread(() =>
        {
            try
            {
                created = factory();
                _ = created.Handle;
                created.BeginInvoke(new Action(() => ready.TrySetResult(created)));
                Application.Run();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
            finally { created?.Dispose(); }
        });
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.IsBackground = true;
        uiThread.Start();

        Form form = await ready.Task;
        try { await body(form); }
        finally
        {
            try { if (form.IsHandleCreated) form.Invoke(() => Application.ExitThread()); } catch (Exception ex) { _ = ex; /* thread exit cleanup */ }
            uiThread.Join(2000);
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch (Exception ex) { _ = ex; /* temp cleanup */ }
    }
}
