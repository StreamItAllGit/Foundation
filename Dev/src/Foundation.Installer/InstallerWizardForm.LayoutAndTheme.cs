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
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts?.Dispose();
            _cts = null;
        }
        base.Dispose(disposing);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        InstallerWizardControls.EnableDarkTitleBar(Handle);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateLayoutBounds();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateLayoutBounds();
    }

    private bool ShouldSuppressModalDialogs =>
        (_ctx?.ShouldSuppressHostMutations ?? (_coreEngine?.IsStage1Isolated ?? false)) ||
        IsTruthyEnvFlag("FOUNDATION_SUPPRESS_MODALS") ||
        !Environment.UserInteractive;

    private static bool IsTruthyEnvFlag(string name)
    {
        string? val = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(val)) return false;
        string trimmed = val.Trim();
        return trimmed.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private void InitializeSecondaryDestinationControls(string? initialSecondaryDir)
    {
        string secLabel = _secondaryPickerOptions?.Label ?? "Companion Directory:";
        string secDefault = initialSecondaryDir ?? _secondaryPickerOptions?.DefaultPath ?? string.Empty;
        _lblSecondaryDirHeader = InstallerWizardControls.CreateSectionLabel(secLabel, new Point(22, 150));
        _lblSecondaryDirHeader.Visible = _hasSecondaryPicker;

        _txtSecondaryDir = InstallerWizardControls.CreateDarkPathTextBox(secDefault, new Point(22, 172), 502);
        _txtSecondaryDir.PlaceholderText = _secondaryPickerOptions?.Placeholder ?? string.Empty;
        _txtSecondaryDir.Visible = _hasSecondaryPicker;

        _btnSecondaryBrowse = InstallerWizardControls.CreateFlatButton(
            "Browse...",
            new Point(532, 170),
            new Size(96, 29),
            _bgCard,
            TextPrimary,
            BorderSubtle);
        _btnSecondaryBrowse.Visible = _hasSecondaryPicker;
        _btnSecondaryBrowse.Click += OnSecondaryBrowseClicked;
    }

    private void InitializeOptionsAndFooterControls(
        bool initialCreateShortcuts,
        bool initialAutoStart,
        bool initialLaunchAfter,
        bool initialPurgeData,
        string initialProfileId,
        bool initialMigrateLegacy)
    {
        _optionsPanel = new Panel
        {
            Location = new Point(22, _hasSecondaryPicker ? 212 : 154),
            Size = new Size(606, 152),
            BackColor = _bgCard,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        var lblOptionsHeader = new Label
        {
            Text = _isUninstallMode ? "Uninstall Options" : "Installation Profile & Options",
            Font = new Font("Segoe UI Semibold", 8.75f, FontStyle.Bold),
            ForeColor = TextMuted,
            AutoSize = true,
            Location = new Point(16, 10)
        };

        bool isFullProfile = string.Equals(initialProfileId, "full", StringComparison.OrdinalIgnoreCase);
        _lblProfileHeader = new Label
        {
            Text = "Profile:",
            Font = new Font("Segoe UI Semibold", 9.0f, FontStyle.Bold),
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(16, 34),
            Visible = !_isUninstallMode
        };
        _rbProfileLightweight = InstallerWizardControls.CreateDarkRadioButton("Lightweight (Core)", 82, 32, !isFullProfile);
        _rbProfileLightweight.Visible = !_isUninstallMode;
        _rbProfileFull = InstallerWizardControls.CreateDarkRadioButton("Full (All Components)", 228, 32, isFullProfile);
        _rbProfileFull.Visible = !_isUninstallMode;

        int nextY = 62;
        _chkShortcuts = new CheckBox
        {
            Text = $"Create Desktop and Start Menu shortcuts for {_displayName}",
            Checked = initialCreateShortcuts,
            AutoSize = true,
            ForeColor = TextPrimary,
            Location = new Point(16, nextY),
            Visible = !_isUninstallMode
        };
        if (!_isUninstallMode) nextY += 26;

        _chkAutoStart = new CheckBox
        {
            Text = $"Add {_displayName} shortcut to Windows Startup folder",
            Checked = _allowAutoStart && initialAutoStart,
            Enabled = !_isUninstallMode && _allowAutoStart,
            AutoSize = true,
            ForeColor = TextPrimary,
            Location = new Point(16, nextY),
            Visible = !_isUninstallMode && _allowAutoStart
        };
        if (!_isUninstallMode && _allowAutoStart) nextY += 26;

        _chkLaunchAfter = new CheckBox
        {
            Text = $"Launch {_displayName} immediately after setup completes",
            Checked = initialLaunchAfter,
            AutoSize = true,
            ForeColor = TextPrimary,
            Location = new Point(16, nextY),
            Visible = !_isUninstallMode
        };
        if (!_isUninstallMode) nextY += 26;

        _chkMigrateLegacy = InstallerWizardControls.CreateDarkCheckBox(
            "Migrate legacy <PROGRAMS_ROOT>\\ installation (opt-in)",
            16,
            nextY,
            initialMigrateLegacy);
        _chkMigrateLegacy.Visible = _showLegacyMigration;
        if (_showLegacyMigration) nextY += 26;

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

        _optionsPanel.Height = _isUninstallMode ? 128 : Math.Max(128, nextY + 12);
        _optionsPanel.Controls.Add(lblOptionsHeader);
        _optionsPanel.Controls.Add(_lblProfileHeader);
        _optionsPanel.Controls.Add(_rbProfileLightweight);
        _optionsPanel.Controls.Add(_rbProfileFull);
        _optionsPanel.Controls.Add(_chkShortcuts);
        _optionsPanel.Controls.Add(_chkAutoStart);
        _optionsPanel.Controls.Add(_chkLaunchAfter);
        _optionsPanel.Controls.Add(_chkMigrateLegacy);
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
        Controls.Add(_lblSecondaryDirHeader);
        Controls.Add(_txtSecondaryDir);
        Controls.Add(_btnSecondaryBrowse);
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
        _lblSecondaryDirHeader.BringToFront();
        _txtSecondaryDir.BringToFront();
        _btnSecondaryBrowse.BringToFront();
        _optionsPanel.BringToFront();
        _lblStatus.BringToFront();
        _progressBar.BringToFront();
        _btnToggleDetails.BringToFront();
        _txtLog.BringToFront();

        base.AcceptButton = _btnPrimary;
        base.CancelButton = _btnCancel;
    }

    public void ToggleDetailsDrawer() => SetDetailsExpanded(!_isDetailsExpanded);

    public void ToggleDetails() => SetDetailsExpanded(!_isDetailsExpanded);

    private int ComputeTargetClientHeight(bool expanded)
    {
        int baseBottom = (_hasSecondaryPicker ? 212 : 154) + _optionsPanel.Height + 12 + 78;
        int collapsed = Math.Max(CollapsedClientHeight, baseBottom + 64);
        return expanded ? collapsed + 110 : collapsed;
    }

    public void SetDetailsExpanded(bool expanded)
    {
        _isDetailsExpanded = expanded;
        _txtLog.Visible = expanded;
        _btnToggleDetails.Text = expanded ? "[v] Hide Details" : "[>] Show Details";
        ClientSize = new Size(
            ClientSize.Width > 0 ? ClientSize.Width : DefaultClientWidth,
            ComputeTargetClientHeight(expanded));
        UpdateLayoutBounds();
    }

    public void UpdateLayoutBounds()
    {
        if (!_controlsInitialized)
            return;

        int clientW = ClientSize.Width > 0 ? ClientSize.Width : DefaultClientWidth;
        int margin = 22;

        int headerW = _headerPanel.ClientSize.Width > 0 ? _headerPanel.ClientSize.Width : clientW;
        int badgeWidth = _lblModeBadge.PreferredWidth > 0
            ? _lblModeBadge.PreferredWidth
            : (_lblModeBadge.Width > 0 ? _lblModeBadge.Width : 128);
        _lblModeBadge.Location = new Point(Math.Max(margin, headerW - margin - badgeWidth), 16);

        int titleWidth = Math.Max(40, _lblModeBadge.Left - 12 - _lblTitle.Left);
        int titleHeight = Math.Max(28, TextRenderer.MeasureText("Mg", _lblTitle.Font).Height + 4);
        _lblTitle.Size = new Size(titleWidth, titleHeight);

        int subWidth = Math.Max(40, _lblModeBadge.Left - 12 - _lblSub.Left);
        int subHeight = Math.Max(20, TextRenderer.MeasureText("Mg", _lblSub.Font).Height + 4);
        _lblSub.Size = new Size(subWidth, subHeight);

        int contentW = Math.Max(200, clientW - (margin * 2));
        _btnBrowse.Location = new Point(clientW - margin - _btnBrowse.Width, 112);
        _txtInstallDir.Location = new Point(margin, 114);
        _txtInstallDir.Width = Math.Max(120, _btnBrowse.Left - 8 - margin);

        if (_hasSecondaryPicker)
        {
            _lblSecondaryDirHeader.Location = new Point(margin, 150);
            _btnSecondaryBrowse.Location = new Point(clientW - margin - _btnSecondaryBrowse.Width, 170);
            _txtSecondaryDir.Location = new Point(margin, 172);
            _txtSecondaryDir.Width = Math.Max(120, _btnSecondaryBrowse.Left - 8 - margin);
        }

        int optionsY = _hasSecondaryPicker ? 212 : 154;
        _optionsPanel.Location = new Point(margin, optionsY);
        _optionsPanel.Width = contentW;

        int belowOptionsY = optionsY + _optionsPanel.Height + 12;
        _lblStatus.Location = new Point(margin, belowOptionsY);
        _lblStatus.Width = contentW;

        _progressBar.Location = new Point(margin, belowOptionsY + 24);
        _progressBar.Width = contentW;

        _btnToggleDetails.Location = new Point(margin, belowOptionsY + 48);

        _txtLog.Location = new Point(margin, belowOptionsY + 78);
        _txtLog.Size = new Size(contentW, 102);

        int targetH = ComputeTargetClientHeight(_isDetailsExpanded);
        if (ClientSize.Height != targetH)
        {
            ClientSize = new Size(clientW, targetH);
        }

        int footerW = _footerPanel.ClientSize.Width > 0 ? _footerPanel.ClientSize.Width : clientW;
        int footerH = _footerPanel.ClientSize.Height > 0 ? _footerPanel.ClientSize.Height : 56;
        int btnY = Math.Max(6, (footerH - _btnPrimary.Height) / 2);

        _btnPrimary.Location = new Point(Math.Max(margin, footerW - margin - _btnPrimary.Width), btnY);
        _btnCancel.Location = new Point(Math.Max(margin, _btnPrimary.Left - 10 - _btnCancel.Width), btnY);
    }
}
