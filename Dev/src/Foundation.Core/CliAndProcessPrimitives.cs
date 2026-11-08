// @module: Foundation.Core.CliAndProcessPrimitives
// @purpose: Shared CLI flag/option/positional extraction helpers, byte size formatter, 64 KB BoundedTailStream, and 4 MB CappedSharedLogSink.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text
// @exports: CliArgumentHelper, BoundedTailStream, CappedSharedLogSink

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Foundation.Core;

public static class CliArgumentHelper
{
    public static bool HasFlag(IReadOnlyList<string> args, string flag) =>
        args != null && args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    public static string? GetOption(IReadOnlyList<string> args, string optionName)
    {
        if (args == null) return null;
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    public static string? GetPositional(IReadOnlyList<string> args, int index, Func<string, bool>? isValueOption = null)
    {
        if (args == null) return null;
        int cur = 0;
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                if (isValueOption != null && isValueOption(a) && i + 1 < args.Count)
                    i++;
                continue;
            }
            if (cur == index)
                return a;
            cur++;
        }
        return null;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double val = bytes;
        int idx = 0;
        while (val >= 1024.0 && idx < units.Length - 1)
        {
            val /= 1024.0;
            idx++;
        }
        return idx == 0 ? $"{bytes:N0} B" : $"{val:F2} {units[idx]}";
    }
}

public sealed class BoundedTailStream : Stream
{
    private readonly byte[] _ring;
    private int _writePos;
    private long _totalBytesWritten;

    public BoundedTailStream(int capacityBytes = 65536)
    {
        _ring = new byte[Math.Max(1024, capacityBytes)];
    }

    public long TotalBytesWritten => _totalBytesWritten;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Math.Min(_totalBytesWritten, _ring.Length);
    public override long Position { get => Length; set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) =>
        Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        int cap = _ring.Length;
        if (buffer.Length >= cap)
        {
            buffer[^cap..].CopyTo(_ring);
            _writePos = 0;
            _totalBytesWritten += buffer.Length;
            return;
        }

        int firstPart = Math.Min(buffer.Length, cap - _writePos);
        buffer[..firstPart].CopyTo(_ring.AsSpan(_writePos, firstPart));
        int remaining = buffer.Length - firstPart;
        if (remaining > 0)
        {
            buffer.Slice(firstPart, remaining).CopyTo(_ring.AsSpan(0, remaining));
            _writePos = remaining;
        }
        else
        {
            _writePos = (_writePos + firstPart) % cap;
        }
        _totalBytesWritten += buffer.Length;
    }

    public byte[] ToTailBytes()
    {
        if (_totalBytesWritten == 0) return Array.Empty<byte>();
        if (_totalBytesWritten < _ring.Length)
        {
            byte[] slice = new byte[(int)_totalBytesWritten];
            Array.Copy(_ring, 0, slice, 0, slice.Length);
            return slice;
        }

        byte[] result = new byte[_ring.Length];
        int tailLen = _ring.Length - _writePos;
        Array.Copy(_ring, _writePos, result, 0, tailLen);
        Array.Copy(_ring, 0, result, tailLen, _writePos);
        return result;
    }

    public string ToTailString(Encoding? encoding = null) =>
        (encoding ?? Encoding.UTF8).GetString(ToTailBytes());

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

public sealed class CappedSharedLogSink
{
    private readonly string _logFilePath;
    private readonly long _maxBytes;
    private readonly object _sync = new();

    public CappedSharedLogSink(string logFilePath, long maxBytes = 4L * 1024 * 1024)
    {
        _logFilePath = SandboxEnvironment.NormalizeLongPath(logFilePath);
        _maxBytes = Math.Max(65536L, maxBytes);
    }

    public void AppendLine(string line)
    {
        lock (_sync)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var fi = new FileInfo(_logFilePath);
                if (fi.Exists && fi.Length >= _maxBytes)
                {
                    string bak = _logFilePath + ".1";
                    File.Move(_logFilePath, bak, overwrite: true);
                }

                using var fs = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                byte[] bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                fs.Write(bytes, 0, bytes.Length);
            }
            catch { }
        }
    }
}
