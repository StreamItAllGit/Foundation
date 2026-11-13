// @module: Foundation.Installer.UI.Wizard.LayoutAndTheme
// @purpose: Options/footer control initialization, DWM dark title bar styling, and responsive layout bounds for InstallerWizardForm.
// @depends-on: System, System.Drawing, System.Windows.Forms
// @exports: InstallerWizardForm (partial: InitializeOptionsAndFooterControls, UpdateLayoutBounds, ToggleDetailsDrawer, SetDetailsExpanded)

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Foundation.Installer;

public sealed partial class InstallerWizardForm
{
    private void InitializeOptionsAndFooterControls(
        bool initialCreateShortcuts,
        bool initialAutoStart,
        bool initialLaunchAfter,
        bool initialPurgeData)
    {
        _optionsPanel = new Panel
        {
            Location = new Point(22, 154),
            Size = new Size(606, 128),
            BackColor = _bgCard,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        var lblOptionsHeader = new Label
        {
            Text = _isUninstallMode ? "Uninstall Options" : "Advanced Options",
            Font = new Font("Segoe UI Semibold", 8.75f, FontStyle.Bold),
            ForeColor = TextMuted,
            AutoSize = true,
            Location = new Point(16, 10)
        };

        _chkShortcuts = new CheckBox
        {
            Text = $"Create Desktop and Start Menu shortcuts for {_displayName}",
            Checked = initialCreateShortcuts,
            AutoSize = true,
            ForeColor = TextPrimary,
            Location = new Point(16, _allowAutoStart ? 36 : 42),
            Visible = !_isUninstallMode
        };

        _chkAutoStart = new CheckBox
        {
            Text = $"Add {_displayName} shortcut to Windows Startup folder",
            Checked = _allowAutoStart && initialAutoStart,
            Enabled = !_isUninstallMode && _allowAutoStart,
            AutoSize = true,
            ForeColor = TextPrimary,
            Location = new Point(16, 64),
            Visible = !_isUninstallMode && _allowAutoStart
        };

        _chkLaunchAfter = new CheckBox
        {
            Text = $"Launch {_displayName} immediately after setup completes",
            Checked = initialLaunchAfter,
            AutoSize = true,
            ForeColor = TextPrimary,
            Location = new Point(16, _allowAutoStart ? 92 : 74),
            Visible = !_isUninstallMode
        };

        _chkPurgeData = new CheckBox
        {
            Text = "Also delete user saves, settings.json, logs\\, and backups\\ (Purge all data)",
            Checked = initialPurgeData,
            AutoSize = true,
            ForeColor = AccentDanger,
            Font = new Font("Segoe UI Semibold", 9.25f, FontStyle.Bold),
            Location = new Point(16, 48),
            Visible = _isUninstallMode
        };

        _optionsPanel.Controls.Add(lblOptionsHeader);
        _optionsPanel.Controls.Add(_chkShortcuts);
        _optionsPanel.Controls.Add(_chkAutoStart);
        _optionsPanel.Controls.Add(_chkLaunchAfter);
        _optionsPanel.Controls.Add(_chkPurgeData);

        _lblStatus = new Label
        {
            Text = _isUninstallMode
                ? $"Ready to uninstall {_displayName}."
                : $"Ready to install or upgrade {_displayName}.",
            AutoSize = false,
            Location = new Point(22, 294),
            Size = new Size(606, 22),
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 9.0f, FontStyle.Regular),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(22, 318),
            Size = new Size(606, 18),
            Style = ProgressBarStyle.Blocks,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        _btnToggleDetails = new Button
        {
            Text = "[>] Show Details",
            Location = new Point(22, 342),
            Size = new Size(132, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = _bgPrimary,
            ForeColor = _accentColor,
            Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _btnToggleDetails.FlatAppearance.BorderSize = 0;
        _btnToggleDetails.Click += (_, _) => SetDetailsExpanded(!_isDetailsExpanded);

        _txtLog = new TextBox
        {
            Location = new Point(22, 372),
            Size = new Size(606, 102),
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

        _footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Size = new Size(DefaultClientWidth, 56),
            BackColor = _bgCard,
            Padding = new Padding(22, 10, 22, 10)
        };

        _btnCancel = new Button
        {
            Text = "Cancel",
            Size = new Size(104, 34),
            Location = new Point(376, 11),
            FlatStyle = FlatStyle.Flat,
            BackColor = _bgPrimary,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _btnCancel.FlatAppearance.BorderColor = BorderSubtle;
        _btnCancel.Click += OnCancelClicked;

        _btnPrimary = new Button
        {
            Text = _isUninstallMode ? "Uninstall" : "Install Now",
            Size = new Size(138, 34),
            Location = new Point(490, 11),
            FlatStyle = FlatStyle.Flat,
            BackColor = _isUninstallMode ? AccentDanger : _accentColor,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _btnPrimary.FlatAppearance.BorderSize = 0;
        _btnPrimary.Click += async (_, _) => await OnPrimaryActionClickedAsync();

        _footerPanel.Controls.Add(_btnCancel);
        _footerPanel.Controls.Add(_btnPrimary);
        _footerPanel.Resize += (_, _) => UpdateLayoutBounds();

        Controls.Add(_bodyPanel);
        Controls.Add(_lblDirHeader);
        Controls.Add(_txtInstallDir);
        Controls.Add(_btnBrowse);
        Controls.Add(_optionsPanel);
        Controls.Add(_lblStatus);
        Controls.Add(_progressBar);
        Controls.Add(_btnToggleDetails);
        Controls.Add(_txtLog);
        Controls.Add(_footerPanel);
        Controls.Add(_headerPanel);

        _lblDirHeader.BringToFront();
        _txtInstallDir.BringToFront();
        _btnBrowse.BringToFront();
        _optionsPanel.BringToFront();
        _lblStatus.BringToFront();
        _progressBar.BringToFront();
        _btnToggleDetails.BringToFront();
        _txtLog.BringToFront();

        base.AcceptButton = _btnPrimary;
        base.CancelButton = _btnCancel;

        if (_showcaseAutoCloseMs > 0)
        {
            ExitCode = 0;
            _txtInstallDir.Enabled = false;
            _btnBrowse.Enabled = false;
            _chkShortcuts.Enabled = false;
            _chkAutoStart.Enabled = false;
            _chkLaunchAfter.Enabled = false;
            _chkPurgeData.Enabled = false;
            _btnPrimary.Enabled = false;
            _lblStatus.Text = $"Visible test showcase (dry preview — auto-closing in {_showcaseAutoCloseMs / 1000.0:F1}s)...";
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryApplyDarkModeTitleBar();
        UpdateLayoutBounds();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        UpdateLayoutBounds();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateLayoutBounds();

        if (_showcaseAutoCloseMs > 0 && _showcaseTimer is null)
        {
            Activate();
            _showcaseTimer = new System.Windows.Forms.Timer { Interval = Math.Max(200, _showcaseAutoCloseMs) };
            _showcaseTimer.Tick += (_, _) =>
            {
                _showcaseTimer?.Stop();
                ExitCode = 0;
                Close();
            };
            _showcaseTimer.Start();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateLayoutBounds();
    }

    private void TryApplyDarkModeTitleBar()
    {
        if (!OperatingSystem.IsWindows() || !IsHandleCreated)
            return;

        try
        {
            int useDark = 1;
            int hr = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
            if (hr != 0)
            {
                DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref useDark, sizeof(int));
            }
        }
        catch { }
    }

    public void ToggleDetailsDrawer() => SetDetailsExpanded(!_isDetailsExpanded);

    public void ToggleDetails() => SetDetailsExpanded(!_isDetailsExpanded);

    public void SetDetailsExpanded(bool expanded)
    {
        _isDetailsExpanded = expanded;
        _txtLog.Visible = expanded;
        _btnToggleDetails.Text = expanded ? "[v] Hide Details" : "[>] Show Details";
        ClientSize = new Size(
            ClientSize.Width > 0 ? ClientSize.Width : DefaultClientWidth,
            expanded ? ExpandedClientHeight : CollapsedClientHeight);
        UpdateLayoutBounds();
    }

    public void UpdateLayoutBounds()
    {
        if (!_controlsInitialized)
            return;

        int clientW = ClientSize.Width > 0 ? ClientSize.Width : DefaultClientWidth;
        int margin = 22;

        int headerW = _headerPanel.ClientSize.Width > 0 ? _headerPanel.ClientSize.Width : clientW;
        int badgeWidth = Math.Max(_lblModeBadge.Width, _lblModeBadge.PreferredWidth);
        if (badgeWidth <= 0) badgeWidth = 128;
        _lblModeBadge.Location = new Point(Math.Max(margin, headerW - margin - badgeWidth), 16);

        int contentW = Math.Max(200, clientW - (margin * 2));
        _btnBrowse.Location = new Point(clientW - margin - _btnBrowse.Width, 112);
        _txtInstallDir.Location = new Point(margin, 114);
        _txtInstallDir.Width = Math.Max(120, _btnBrowse.Left - 8 - margin);

        _optionsPanel.Location = new Point(margin, 154);
        _optionsPanel.Width = contentW;

        _lblStatus.Location = new Point(margin, 294);
        _lblStatus.Width = contentW;

        _progressBar.Location = new Point(margin, 318);
        _progressBar.Width = contentW;

        _btnToggleDetails.Location = new Point(margin, 342);

        _txtLog.Location = new Point(margin, 372);
        _txtLog.Size = new Size(contentW, 102);

        int footerW = _footerPanel.ClientSize.Width > 0 ? _footerPanel.ClientSize.Width : clientW;
        int footerH = _footerPanel.ClientSize.Height > 0 ? _footerPanel.ClientSize.Height : 56;
        int btnY = Math.Max(6, (footerH - _btnPrimary.Height) / 2);

        _btnPrimary.Location = new Point(Math.Max(margin, footerW - margin - _btnPrimary.Width), btnY);
        _btnCancel.Location = new Point(Math.Max(margin, _btnPrimary.Left - 10 - _btnCancel.Width), btnY);
    }
}
