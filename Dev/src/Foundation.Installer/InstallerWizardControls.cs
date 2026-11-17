// @module: Foundation.Installer.UI.Controls
// @purpose: Provides color parsing, DWM dark titlebar styling, and factory helpers for the 650x434 / 650x544 Deep Slate Dark WinForms Setup Wizard.
// @depends-on: System, System.Drawing, System.Globalization, System.Runtime.InteropServices, System.Windows.Forms
// @exports: InstallerWizardControls

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Foundation.Installer;

/// <summary>
/// Shared dark-theme control builders and hex color utilities for Foundation.Installer's Setup Wizard (Standard 09).
/// </summary>
public static class InstallerWizardControls
{
    public static readonly Color TextPrimary = Color.FromArgb(236, 240, 248);
    public static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
    public static readonly Color BgInput = Color.FromArgb(17, 21, 29);
    public static readonly Color BorderSubtle = Color.FromArgb(42, 51, 68);
    public static readonly Color AccentEmerald = Color.FromArgb(16, 185, 129);
    public static readonly Color AccentDanger = Color.FromArgb(239, 68, 68);
    private const int DwmUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void EnableDarkTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref useDark, sizeof(int));
        }
        catch (Exception ex)
        {
            _ = ex; // Systems without DWM dark titlebar support proceed with default window frame.
        }
    }

    public static Color ParseHexColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        string cleaned = hex.Trim().TrimStart('#');
        if (cleaned.Length == 6 &&
            int.TryParse(cleaned[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int r) &&
            int.TryParse(cleaned.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int g) &&
            int.TryParse(cleaned.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b))
        {
            return Color.FromArgb(r, g, b);
        }

        return fallback;
    }

    public static CheckBox CreateDarkCheckBox(string text, int x, int y, bool isChecked, Color? foreColor = null)
    {
        return new CheckBox
        {
            Text = text,
            Checked = isChecked,
            AutoSize = true,
            Location = new Point(x, y),
            ForeColor = foreColor ?? TextPrimary,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular),
            Cursor = Cursors.Hand
        };
    }

    public static RadioButton CreateDarkRadioButton(string text, int x, int y, bool isChecked, Color? foreColor = null)
    {
        return new RadioButton
        {
            Text = text,
            Checked = isChecked,
            AutoSize = true,
            Location = new Point(x, y),
            ForeColor = foreColor ?? TextPrimary,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular),
            Cursor = Cursors.Hand
        };
    }

    public static TextBox CreateDarkPathTextBox(string text, Point location, int width)
    {
        return new TextBox
        {
            Text = text,
            Location = location,
            Width = width,
            Height = 29,
            BackColor = BgInput,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9.5f, FontStyle.Regular),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
    }

    public static Label CreateSectionLabel(string text, Point location, Color? foreColor = null)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            ForeColor = foreColor ?? TextPrimary,
            AutoSize = true,
            Location = location
        };
    }

    public static Button CreateFlatButton(
        string text,
        Point location,
        Size size,
        Color backColor,
        Color foreColor,
        Color? borderColor = null,
        bool bold = false)
    {
        var btn = new Button
        {
            Text = text,
            Location = location,
            Size = size,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", 9.25f, bold ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        btn.FlatAppearance.BorderSize = borderColor.HasValue ? 1 : 0;
        if (borderColor.HasValue)
        {
            btn.FlatAppearance.BorderColor = borderColor.Value;
        }
        return btn;
    }

    public static TextBox CreateDarkLogTextBox(Point location, Size size)
    {
        return new TextBox
        {
            Location = location,
            Size = size,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = BgInput,
            ForeColor = TextMuted,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.5f, FontStyle.Regular),
            Visible = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
    }

    public static ToolTip CreateDarkTooltipController() =>
        new() { AutoPopDelay = 8000, InitialDelay = 300, ReshowDelay = 100, ShowAlways = true };

    public static void ApplyTooltips(
        ToolTip controller,
        IReadOnlyDictionary<string, InstallerMarkupTooltip>? tooltips,
        IReadOnlyDictionary<string, Control> controlMap)
    {
        if (tooltips is null || tooltips.Count == 0) return;
        foreach (KeyValuePair<string, Control> kv in controlMap)
        {
            if (tooltips.TryGetValue(kv.Key, out InstallerMarkupTooltip? tip))
            {
                string formatted = tip.FormatTwoLine();
                if (!string.IsNullOrWhiteSpace(formatted))
                    controller.SetToolTip(kv.Value, formatted);
            }
        }
    }

    public static Image? TryLoadImage(string? pathOrResource, Assembly? assembly)
    {
        if (string.IsNullOrWhiteSpace(pathOrResource)) return null;
        string trimmed = pathOrResource.Trim();
        try
        {
            string resolved = Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(AppContext.BaseDirectory, trimmed);
            if (File.Exists(resolved))
            {
                using var fs = File.OpenRead(resolved);
                using var img = Image.FromStream(fs);
                return new Bitmap(img);
            }

            if (assembly is not null)
            {
                string? resName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(resName))
                {
                    using Stream? stream = assembly.GetManifestResourceStream(resName);
                    if (stream is not null)
                    {
                        using var img = Image.FromStream(stream);
                        return new Bitmap(img);
                    }
                }
            }
        }
        catch (Exception ex) { _ = ex; /* optional logo load failure falls back cleanly */ }
        return null;
    }

    public static Icon? TryLoadIcon(string? pathOrResource, Assembly? assembly)
    {
        if (string.IsNullOrWhiteSpace(pathOrResource)) return null;
        string trimmed = pathOrResource.Trim();
        try
        {
            string resolved = Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(AppContext.BaseDirectory, trimmed);
            if (File.Exists(resolved)) return new Icon(resolved);

            if (assembly is not null)
            {
                string? resName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(resName))
                {
                    using Stream? stream = assembly.GetManifestResourceStream(resName);
                    if (stream is not null) return new Icon(stream);
                }
            }
        }
        catch (Exception ex) { _ = ex; /* optional logo load failure falls back cleanly */ }
        return null;
    }
}
