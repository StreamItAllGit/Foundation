// @module: Foundation.Installer.UI.Controls
// @purpose: Lightweight NativeAOT-safe control abstractions, hex color utilities, and DWM dark titlebar helpers replacing WinForms Control/Color types.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, System.Linq, System.Reflection
// @exports: WizardPoint, WizardSize, WizardColor, WizardControl, WizardPanel, WizardLabel, WizardTextBox, WizardButton, WizardCheckBox, WizardRadioButton, WizardProgressBar, InstallerWizardControls

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Foundation.Installer;

public readonly record struct WizardPoint(int X, int Y);

public readonly record struct WizardSize(int Width, int Height);

public readonly record struct WizardColor(byte R, byte G, byte B, byte A = 255)
{
    public static WizardColor White => new(255, 255, 255, 255);
    public static WizardColor Transparent => new(0, 0, 0, 0);

    public static WizardColor FromRgb(int r, int g, int b) =>
        new((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255), 255);

    public uint ToColorRef() => (uint)(R | (G << 8) | (B << 16));
}

public class WizardControl : IDisposable
{
    private string _text = string.Empty;
    private WizardPoint _location;
    private WizardSize _size;
    private bool _visible = true;
    private bool _enabled = true;
    private WizardColor _foreColor = InstallerWizardControls.TextPrimary;
    private WizardColor _backColor = WizardColor.Transparent;
    private WizardColor? _borderColor;
    private Action<WizardControl, string>? _syncCallback;

    public string Name { get; set; } = string.Empty;
    public IntPtr NativeHwnd { get; internal set; }
    public WizardControl? Parent { get; internal set; }
    public string FontFamily { get; set; } = "Segoe UI";
    public float FontSize { get; set; } = 9.0f;
    public bool Bold { get; set; }
    public bool AutoSize { get; set; }
    public bool AutoEllipsis { get; set; }
    public int BorderSize { get; set; }
    public string? TooltipText { get; internal set; }

    public string Text
    {
        get => _text;
        set
        {
            string next = value ?? string.Empty;
            if (string.Equals(_text, next, StringComparison.Ordinal)) return;
            _text = next;
            _syncCallback?.Invoke(this, nameof(Text));
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public WizardPoint Location
    {
        get => _location;
        set { if (_location != value) { _location = value; _syncCallback?.Invoke(this, nameof(Location)); } }
    }

    public int Left { get => _location.X; set => Location = new WizardPoint(value, _location.Y); }
    public int Top { get => _location.Y; set => Location = new WizardPoint(_location.X, value); }

    public WizardSize Size
    {
        get => _size;
        set { if (_size != value) { _size = value; _syncCallback?.Invoke(this, nameof(Size)); } }
    }

    public WizardSize ClientSize => _size;
    public int Width { get => _size.Width; set => Size = new WizardSize(value, _size.Height); }
    public int Height { get => _size.Height; set => Size = new WizardSize(_size.Width, value); }
    public int Right => _location.X + _size.Width;
    public int Bottom => _location.Y + _size.Height;

    public bool Visible
    {
        get => _visible;
        set { if (_visible != value) { _visible = value; _syncCallback?.Invoke(this, nameof(Visible)); } }
    }

    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled != value) { _enabled = value; _syncCallback?.Invoke(this, nameof(Enabled)); } }
    }

    public WizardColor ForeColor
    {
        get => _foreColor;
        set { if (_foreColor != value) { _foreColor = value; _syncCallback?.Invoke(this, nameof(ForeColor)); } }
    }

    public WizardColor BackColor
    {
        get => _backColor;
        set { if (_backColor != value) { _backColor = value; _syncCallback?.Invoke(this, nameof(BackColor)); } }
    }

    public WizardColor? BorderColor
    {
        get => _borderColor;
        set { if (_borderColor != value) { _borderColor = value; _syncCallback?.Invoke(this, nameof(BorderColor)); } }
    }

    public event EventHandler? TextChanged;

    internal void AttachSyncCallback(Action<WizardControl, string> callback) => _syncCallback = callback;

    internal void SetTextFromNative(string value)
    {
        string next = value ?? string.Empty;
        if (string.Equals(_text, next, StringComparison.Ordinal)) return;
        _text = next;
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    public virtual void Dispose() { }
}

public sealed class WizardPanel : WizardControl
{
    public List<WizardControl> Controls { get; } = new();

    public void AddControl(WizardControl child)
    {
        child.Parent = this;
        Controls.Add(child);
    }
}

public sealed class WizardLabel : WizardControl
{
    public int PreferredWidth => InstallerWizardControls.MeasureLabelWidth(Text, FontFamily, FontSize, Bold);
}

public sealed class WizardTextBox : WizardControl
{
    public bool Multiline { get; set; }
    public bool ReadOnly { get; set; }
    public string PlaceholderText { get; set; } = string.Empty;

    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Text += text;
    }
}

public sealed class WizardButton : WizardControl
{
    public bool TextAlignLeft { get; set; }
    public event EventHandler? Click;
    internal void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);
}

public sealed class WizardCheckBox : WizardControl
{
    private bool _checked;
    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class WizardRadioButton : WizardControl
{
    private bool _checked;
    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            if (_checked && Parent is WizardPanel panel)
            {
                foreach (WizardControl sibling in panel.Controls)
                {
                    if (!ReferenceEquals(sibling, this) && sibling is WizardRadioButton rb && rb._checked)
                    {
                        rb.Checked = false;
                    }
                }
            }
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class WizardProgressBar : WizardControl
{
    private int _value;
    private bool _isMarquee;
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;

    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, Minimum, Maximum);
            if (_value == clamped) return;
            _value = clamped;
        }
    }

    public bool IsMarquee
    {
        get => _isMarquee;
        set => _isMarquee = value;
    }
}

public static class InstallerWizardControls
{
    public static readonly WizardColor TextPrimary = WizardColor.FromRgb(236, 240, 248);
    public static readonly WizardColor TextMuted = WizardColor.FromRgb(148, 163, 184);
    public static readonly WizardColor BgInput = WizardColor.FromRgb(17, 21, 29);
    public static readonly WizardColor BorderSubtle = WizardColor.FromRgb(42, 51, 68);
    public static readonly WizardColor AccentEmerald = WizardColor.FromRgb(16, 185, 129);
    public static readonly WizardColor AccentDanger = WizardColor.FromRgb(239, 68, 68);

    public static void EnableDarkTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindows()) return;
        try
        {
            int useDark = 1;
            _ = Win32Native.DwmSetWindowAttribute(hwnd, Win32Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
        catch (Exception ex)
        {
            _ = ex;
        }
    }

    public static WizardColor ParseHexColor(string? hex, WizardColor fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        string cleaned = hex.Trim().TrimStart('#');
        if (cleaned.Length == 6 &&
            int.TryParse(cleaned[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int r) &&
            int.TryParse(cleaned.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int g) &&
            int.TryParse(cleaned.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b))
        {
            return WizardColor.FromRgb(r, g, b);
        }

        return fallback;
    }

    public static int MeasureLabelWidth(string? text, string fontFamily, float fontSizePt, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (OperatingSystem.IsWindows())
        {
            IntPtr hdc = Win32Native.GetDC(IntPtr.Zero);
            if (hdc != IntPtr.Zero)
            {
                try
                {
                    int pxHeight = -Math.Max(11, (int)Math.Round(fontSizePt * 96.0 / 72.0));
                    IntPtr hfont = Win32Native.CreateFontW(pxHeight, 0, 0, 0, bold ? 700 : 400, 0, 0, 0, 1, 0, 0, 5, 0, fontFamily);
                    if (hfont != IntPtr.Zero)
                    {
                        IntPtr old = Win32Native.SelectObject(hdc, hfont);
                        try
                        {
                            if (Win32Native.GetTextExtentPoint32W(hdc, text, text.Length, out Win32Native.SIZE sz))
                                return Math.Max(16, sz.cx + 4);
                        }
                        finally
                        {
                            _ = Win32Native.SelectObject(hdc, old);
                            _ = Win32Native.DeleteObject(hfont);
                        }
                    }
                }
                finally
                {
                    _ = Win32Native.ReleaseDC(IntPtr.Zero, hdc);
                }
            }
        }
        return (int)Math.Ceiling(text.Length * fontSizePt * (bold ? 0.85 : 0.75)) + 6;
    }

    public static WizardCheckBox CreateDarkCheckBox(string text, int x, int y, bool isChecked, WizardColor? foreColor = null) =>
        new()
        {
            Text = text,
            Checked = isChecked,
            AutoSize = true,
            Location = new WizardPoint(x, y),
            Size = new WizardSize(560, 22),
            ForeColor = foreColor ?? TextPrimary,
            FontFamily = "Segoe UI",
            FontSize = 9.0f
        };

    public static WizardRadioButton CreateDarkRadioButton(string text, int x, int y, bool isChecked, WizardColor? foreColor = null) =>
        new()
        {
            Text = text,
            Checked = isChecked,
            AutoSize = true,
            Location = new WizardPoint(x, y),
            Size = new WizardSize(150, 22),
            ForeColor = foreColor ?? TextPrimary,
            FontFamily = "Segoe UI",
            FontSize = 9.0f
        };

    public static WizardTextBox CreateDarkPathTextBox(string text, WizardPoint location, int width) =>
        new()
        {
            Text = text,
            Location = location,
            Size = new WizardSize(width, 29),
            BackColor = BgInput,
            ForeColor = TextPrimary,
            FontFamily = "Consolas",
            FontSize = 9.5f
        };

    public static WizardLabel CreateSectionLabel(string text, WizardPoint location, WizardColor? foreColor = null) =>
        new()
        {
            Text = text,
            FontFamily = "Segoe UI Semibold",
            FontSize = 9.5f,
            Bold = true,
            AutoSize = true,
            Location = location,
            Size = new WizardSize(Math.Max(120, MeasureLabelWidth(text, "Segoe UI Semibold", 9.5f, true)), 20),
            ForeColor = foreColor ?? TextPrimary
        };

    public static WizardButton CreateFlatButton(
        string text,
        WizardPoint location,
        WizardSize size,
        WizardColor backColor,
        WizardColor foreColor,
        WizardColor? borderColor = null,
        bool bold = false) =>
        new()
        {
            Text = text,
            Location = location,
            Size = size,
            BackColor = backColor,
            ForeColor = foreColor,
            BorderColor = borderColor,
            BorderSize = borderColor.HasValue ? 1 : 0,
            FontFamily = bold ? "Segoe UI Semibold" : "Segoe UI",
            FontSize = 9.25f,
            Bold = bold
        };

    public static void ApplyTooltips(
        IDictionary<WizardControl, string> store,
        IReadOnlyDictionary<string, InstallerMarkupTooltip>? tooltips,
        IReadOnlyDictionary<string, WizardControl> controlMap)
    {
        if (tooltips is null || tooltips.Count == 0) return;
        foreach (KeyValuePair<string, WizardControl> kv in controlMap)
        {
            if (tooltips.TryGetValue(kv.Key, out InstallerMarkupTooltip? tip))
            {
                string formatted = tip.FormatTwoLine();
                if (!string.IsNullOrWhiteSpace(formatted))
                {
                    kv.Value.TooltipText = formatted;
                    store[kv.Value] = formatted;
                }
            }
        }
    }
}
