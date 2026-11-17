// @module: Foundation.Automation.LoopbackServer
// @purpose: Opt-in 127.0.0.1 HTTP/1.1 JSON loopback server enforcing strict Host/Origin (403) and Bearer token (401) security gates.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, System.Net, System.Net.Sockets, System.Security.Cryptography, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, System.Windows.Forms, Foundation.Core
// @exports: AutomationLoopbackServer

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Foundation.Core;

namespace Foundation.Automation;

public sealed class AutomationLoopbackServer : IAsyncDisposable, IDisposable
{
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true, WriteIndented = false };

    private readonly AutomationServerOptions _options;
    private readonly byte[] _expectedTokenHash;
    private readonly Control? _rootControl;
    private readonly IRenderSurfaceSnapshotProvider? _renderProvider;
    private readonly SandboxResolution _sandbox;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private SynchronizationContext? _uiSyncContext;
    private Task? _acceptTask;
    private bool _disposed;

    public int AutomationPort { get; private set; }
    public string BearerToken => _options.BearerToken;
    public bool IsListening { get; private set; }

    public AutomationLoopbackServer(
        AutomationServerOptions options,
        Control? rootControl = null,
        IRenderSurfaceSnapshotProvider? renderProvider = null,
        SandboxResolution? sandbox = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.BearerToken))
            throw new ArgumentException("BearerToken must be non-empty.", nameof(options));

        _expectedTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.BearerToken.Trim()));
        _rootControl = rootControl;
        _renderProvider = renderProvider;
        _sandbox = sandbox ?? SandboxEnvironmentSeam.ResolveCurrent();
        _uiSyncContext = SynchronizationContext.Current;
        _listener = new TcpListener(IPAddress.Loopback, options.AutomationPort);
        AutomationPort = options.AutomationPort;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsListening) return;
        _uiSyncContext ??= SynchronizationContext.Current;
        if (_rootControl is not null && !_rootControl.IsDisposed && !_rootControl.IsHandleCreated && _uiSyncContext is null)
            _ = _rootControl.Handle;
        _listener.Start();
        AutomationPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        IsListening = true;
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { break; }
            _ = Task.Run(() => HandleClientAsync(client, ct), ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            await using NetworkStream stream = client.GetStream();

            var (errCode, errMsg, method, path, headers, body) = await ReadHttpRequestAsync(stream, ct).ConfigureAwait(false);
            if (errCode != 0)
            {
                await WriteJsonResponseAsync(stream, errCode, AutomationResponse<object>.Fail(errMsg ?? "Bad request."), ct).ConfigureAwait(false);
                return;
            }
            if (!IsAllowedLoopbackHost(headers.GetValueOrDefault("host")))
            {
                await WriteJsonResponseAsync(stream, 403, AutomationResponse<object>.Fail("Forbidden: Host must be 127.0.0.1 or localhost."), ct).ConfigureAwait(false);
                return;
            }
            if (headers.TryGetValue("origin", out string? origin) && !IsAllowedLoopbackOrigin(origin))
            {
                await WriteJsonResponseAsync(stream, 403, AutomationResponse<object>.Fail("Forbidden: external Origin header rejected."), ct).ConfigureAwait(false);
                return;
            }
            if (!IsAuthorizedBearer(headers.GetValueOrDefault("authorization")))
            {
                await WriteJsonResponseAsync(stream, 401, AutomationResponse<object>.Fail("Unauthorized: invalid or missing bearer token."), ct).ConfigureAwait(false);
                return;
            }
            await RouteAndRespondAsync(stream, method, path, body, ct).ConfigureAwait(false);
        }
    }

    private async Task RouteAndRespondAsync(NetworkStream stream, string method, string path, string body, CancellationToken ct)
    {
        try
        {
            string route = path.Split('?', 2)[0];
            if (method == "GET" && route == "/v1/health")
            {
                var health = new AutomationHealthPayload(_options.AppId, AutomationPort, "completed", true, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                await WriteJsonResponseAsync(stream, 200, AutomationResponse<AutomationHealthPayload>.Ok(health), ct).ConfigureAwait(false);
            }
            else if (method == "GET" && route == "/v1/tree")
            {
                AutomationControlNode? tree = RunOnUi(() => Win32VirtualInputDriver.InspectTree(_rootControl));
                await WriteJsonResponseAsync(stream, 200, AutomationResponse<AutomationControlNode?>.Ok(tree), ct).ConfigureAwait(false);
            }
            else if (method == "POST" && route == "/v1/pointer")
            {
                bool ok = RunOnUi(() => Win32VirtualInputDriver.ExecutePointer(_rootControl, DeserializeBody<AutomationPointerRequest>(body)));
                var resp = ok ? AutomationResponse<bool>.Ok(true) : AutomationResponse<bool>.Fail("No active root control is attached for pointer input.");
                await WriteJsonResponseAsync(stream, ok ? 200 : 400, resp, ct).ConfigureAwait(false);
            }
            else if (method == "POST" && route == "/v1/keyboard")
            {
                bool ok = RunOnUi(() => Win32VirtualInputDriver.ExecuteKeyboard(_rootControl, DeserializeBody<AutomationKeyboardRequest>(body)));
                var resp = ok ? AutomationResponse<bool>.Ok(true) : AutomationResponse<bool>.Fail("No active root control is attached for keyboard input.");
                await WriteJsonResponseAsync(stream, ok ? 200 : 400, resp, ct).ConfigureAwait(false);
            }
            else if (method == "POST" && route == "/v1/snapshot")
            {
                var snap = RunOnUi(() => WindowSnapshotService.CaptureSnapshot(_rootControl, _renderProvider, DeserializeBody<AutomationSnapshotRequest>(body), _options.AllowedSnapshotRoot, _sandbox));
                await WriteJsonResponseAsync(stream, 200, AutomationResponse<AutomationSnapshotResult>.Ok(snap), ct).ConfigureAwait(false);
            }
            else
            {
                await WriteJsonResponseAsync(stream, 404, AutomationResponse<object>.Fail($"Route not found: {method} {route}"), ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            int code = SandboxEnvironmentSeam.IsContainmentViolation(ex) ? 403 : 400;
            await WriteJsonResponseAsync(stream, code, AutomationResponse<object>.Fail(ex.Message), ct).ConfigureAwait(false);
        }
    }

    private T RunOnUi<T>(Func<T> action)
    {
        if (_rootControl is not null && !_rootControl.IsDisposed && _rootControl.IsHandleCreated && _rootControl.InvokeRequired)
            return (T)_rootControl.Invoke(action);
        if (_uiSyncContext is not null && !ReferenceEquals(SynchronizationContext.Current, _uiSyncContext))
        {
            T? result = default;
            Exception? captured = null;
            _uiSyncContext.Send(_ => { try { result = action(); } catch (Exception ex) { captured = ex; } }, null);
            if (captured is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(captured).Throw();
            return result!;
        }
        return action();
    }

    private static T DeserializeBody<T>(string body) =>
        JsonSerializer.Deserialize<T>(string.IsNullOrWhiteSpace(body) ? "{}" : body, JsonOpts)
        ?? throw new InvalidDataException("Request JSON payload deserialized to null.");

    private bool IsAuthorizedBearer(string? authHeader)
    {
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;
        byte[] actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(authHeader["Bearer ".Length..].Trim()));
        return CryptographicOperations.FixedTimeEquals(actualHash, _expectedTokenHash);
    }

    internal static bool IsAllowedLoopbackHost(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader)) return false;
        string trimmed = hostHeader.Trim();
        int colonIdx = trimmed.LastIndexOf(':');
        string hostOnly = colonIdx > 0 && !trimmed.StartsWith("[", StringComparison.Ordinal) ? trimmed[..colonIdx] : trimmed;
        return hostOnly.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) || hostOnly.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAllowedLoopbackOrigin(string? originHeader)
    {
        if (string.IsNullOrWhiteSpace(originHeader)) return true;
        if (!Uri.TryCreate(originHeader.Trim(), UriKind.Absolute, out Uri? uri)) return false;
        return uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int ErrCode, string? ErrMsg, string Method, string Path, Dictionary<string, string> Headers, string Body)> ReadHttpRequestAsync(
        NetworkStream stream, CancellationToken ct)
    {
        byte[] buffer = new byte[MaxHeaderBytes];
        int totalRead = 0, headerEnd = -1;
        while (totalRead < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct).ConfigureAwait(false);
            if (n == 0) break;
            totalRead += n;
            headerEnd = FindHeaderEnd(buffer, totalRead);
            if (headerEnd >= 0) break;
        }
        if (headerEnd < 0) return (400, "Malformed or oversized HTTP headers.", string.Empty, string.Empty, new(), string.Empty);

        string[] lines = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n", StringSplitOptions.None);
        string[] reqLine = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (reqLine.Length < 2) return (400, "Invalid HTTP request line.", string.Empty, string.Empty, new(), string.Empty);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            int idx = lines[i].IndexOf(':');
            if (idx > 0) headers[lines[i][..idx].Trim()] = lines[i][(idx + 1)..].Trim();
        }

        int contentLen = 0;
        if (headers.TryGetValue("content-length", out string? rawLen) &&
            (!int.TryParse(rawLen, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLen) || contentLen < 0 || contentLen > MaxBodyBytes))
            return (413, "Invalid or oversized Content-Length.", string.Empty, string.Empty, headers, string.Empty);

        byte[] bodyBytes = new byte[contentLen];
        int alreadyInBuf = Math.Min(contentLen, Math.Max(0, totalRead - (headerEnd + 4)));
        if (alreadyInBuf > 0) Buffer.BlockCopy(buffer, headerEnd + 4, bodyBytes, 0, alreadyInBuf);
        while (alreadyInBuf < contentLen)
        {
            int n = await stream.ReadAsync(bodyBytes.AsMemory(alreadyInBuf, contentLen - alreadyInBuf), ct).ConfigureAwait(false);
            if (n == 0) break;
            alreadyInBuf += n;
        }
        return (0, null, reqLine[0].ToUpperInvariant(), reqLine[1], headers, alreadyInBuf > 0 ? Encoding.UTF8.GetString(bodyBytes, 0, alreadyInBuf) : string.Empty);
    }

    private static int FindHeaderEnd(byte[] buf, int len)
    {
        for (int i = 0; i <= len - 4; i++)
            if (buf[i] == '\r' && buf[i + 1] == '\n' && buf[i + 2] == '\r' && buf[i + 3] == '\n') return i;
        return -1;
    }

    private static async Task WriteJsonResponseAsync<T>(NetworkStream stream, int statusCode, AutomationResponse<T> payload, CancellationToken ct)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOpts));
        string reason = statusCode switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 413 => "Payload Too Large", _ => "Error" };
        string head = $"HTTP/1.1 {statusCode} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {jsonBytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
        await stream.WriteAsync(jsonBytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsListening = false;
        _cts.Cancel();
        try { _listener.Stop(); } catch (Exception ex) { _ = ex; /* listener already stopped */ }
        try { _acceptTask?.Wait(500); } catch (Exception ex) { _ = ex; /* accept loop shutdown */ }
        _cts.Dispose();
    }

    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
