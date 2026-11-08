// @module: Foundation.Installer.UI.Controls
// @purpose: Provides color parsing and factory helpers for the 650x434 / 650x544 Deep Slate Dark WinForms Setup Wizard.
// @depends-on: System, System.Drawing, System.Globalization, System.Windows.Forms
// @exports: InstallerWizardControls

using System;
using System.Drawing;
using System.Globalization;
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
}
