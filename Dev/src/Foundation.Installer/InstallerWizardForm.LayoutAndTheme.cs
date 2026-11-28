// @module: Foundation.Installer.UI.Wizard.LayoutAndTheme
// @purpose: Options/footer control initialization, single-profile row collapse, DWM dark title bar styling, and responsive layout bounds for InstallerWizardForm.
// @depends-on: System, System.Collections.Generic, System.Linq
// @exports: InstallerWizardForm (partial: InitializeOptionsAndFooterControls, UpdateLayoutBounds, ToggleDetailsDrawer, SetDetailsExpanded)

using System;
using System.Collections.Generic;
using System.Linq;

namespace Foundation.Installer;

public sealed partial class InstallerWizardForm
{
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

    private void InitializeSecondaryDestinationControls(string? initialSecondaryDir)
    {
        string secLabel = _secondaryPickerOptions?.Label ?? "Companion Directory:";
        string secDefault = initialSecondaryDir ?? _secondaryPickerOptions?.DefaultPath ?? string.Empty;
        _lblSecondaryDirHeader = InstallerWizardControls.CreateSectionLabel(secLabel, new WizardPoint(22, 150), _textPrimary);
        _lblSecondaryDirHeader.BackColor = _bgPrimary;
        _lblSecondaryDirHeader.Visible = _hasSecondaryPicker;

        _txtSecondaryDir = InstallerWizardControls.CreateDarkPathTextBox(secDefault, new WizardPoint(22, 172), 502);
        _txtSecondaryDir.PlaceholderText = _secondaryPickerOptions?.Placeholder ?? string.Empty;
        _txtSecondaryDir.Visible = _hasSecondaryPicker;

        _btnSecondaryBrowse = InstallerWizardControls.CreateFlatButton(
            "Browse...",
            new WizardPoint(532, 170),
            new WizardSize(96, 29),
            _bgCard,
            _textPrimary,
            _borderSubtle);
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
        _optionsPanel = new WizardPanel
        {
            Location = new WizardPoint(22, _hasSecondaryPicker ? 212 : 154),
            Size = new WizardSize(606, 152),
            BackColor = _bgCard,
            BorderColor = _borderSubtle,
            BorderSize = 1
        };

        _lblOptionsHeader = new WizardLabel
        {
            Text = _isUninstallMode ? "Uninstall Options" : "Installation Profile & Options",
            FontFamily = "Segoe UI Semibold",
            FontSize = 8.75f,
            Bold = true,
            ForeColor = TextMuted,
            BackColor = _bgCard,
            AutoSize = true,
            Location = new WizardPoint(16, 10),
            Size = new WizardSize(280, 18)
        };

        bool supportsLightweight = _supportedProfiles.Contains("lightweight", StringComparer.OrdinalIgnoreCase);
        bool supportsFull = _supportedProfiles.Contains("full", StringComparer.OrdinalIgnoreCase);
        bool supportsCustom = _supportedProfiles.Contains("custom", StringComparer.OrdinalIgnoreCase);
        bool showProfileRow = !_isUninstallMode && _supportedProfiles.Count > 1;

        string selectedProfile = _supportedProfiles.FirstOrDefault(p => string.Equals(p, initialProfileId, StringComparison.OrdinalIgnoreCase))
            ?? _supportedProfiles.FirstOrDefault()
            ?? "lightweight";
        bool isCustomProfile = supportsCustom && string.Equals(selectedProfile, "custom", StringComparison.OrdinalIgnoreCase);
        bool isFullProfile = supportsFull && string.Equals(selectedProfile, "full", StringComparison.OrdinalIgnoreCase);
        bool isLightweightProfile = supportsLightweight && string.Equals(selectedProfile, "lightweight", StringComparison.OrdinalIgnoreCase);

        _lblProfileHeader = new WizardLabel
        {
            Text = "Profile:",
            FontFamily = "Segoe UI Semibold",
            FontSize = 9.0f,
            Bold = true,
            ForeColor = _textPrimary,
            BackColor = _bgCard,
            AutoSize = true,
            Location = new WizardPoint(16, 34),
            Size = new WizardSize(58, 20),
            Visible = showProfileRow
        };
        _rbProfileLightweight = InstallerWizardControls.CreateDarkRadioButton("Lightweight (Core)", 82, 32, isLightweightProfile, _textPrimary);
        _rbProfileLightweight.BackColor = _bgCard;
        _rbProfileLightweight.Visible = showProfileRow && supportsLightweight;
        _rbProfileFull = InstallerWizardControls.CreateDarkRadioButton("Full (All Components)", 228, 32, isFullProfile, _textPrimary);
        _rbProfileFull.BackColor = _bgCard;
        _rbProfileFull.Visible = showProfileRow && supportsFull;
        _rbProfileCustom = InstallerWizardControls.CreateDarkRadioButton("Custom", 394, 32, isCustomProfile, _textPrimary);
        _rbProfileCustom.BackColor = _bgCard;
        _rbProfileCustom.Visible = showProfileRow && supportsCustom;

        int nextY = showProfileRow ? 62 : 34;
        _chkShortcuts = InstallerWizardControls.CreateDarkCheckBox(
            $"Create Desktop and Start Menu shortcuts for {_displayName}",
            16,
            nextY,
            initialCreateShortcuts,
            _textPrimary);
        _chkShortcuts.BackColor = _bgCard;
        _chkShortcuts.Visible = !_isUninstallMode;
        if (!_isUninstallMode) nextY += 26;

        _chkAutoStart = InstallerWizardControls.CreateDarkCheckBox(
            $"Add {_displayName} shortcut to Windows Startup folder",
            16,
            nextY,
            _allowAutoStart && initialAutoStart,
            _textPrimary);
        _chkAutoStart.BackColor = _bgCard;
        _chkAutoStart.Enabled = !_isUninstallMode && _allowAutoStart;
        _chkAutoStart.Visible = !_isUninstallMode && _allowAutoStart;
        if (!_isUninstallMode && _allowAutoStart) nextY += 26;

        _chkLaunchAfter = InstallerWizardControls.CreateDarkCheckBox(
            $"Launch {_displayName} immediately after setup completes",
            16,
            nextY,
            initialLaunchAfter,
            _textPrimary);
        _chkLaunchAfter.BackColor = _bgCard;
        _chkLaunchAfter.Visible = !_isUninstallMode;
        if (!_isUninstallMode) nextY += 26;

        _chkMigrateLegacy = InstallerWizardControls.CreateDarkCheckBox(
            "Migrate legacy <PROGRAMS_ROOT>\\ installation (opt-in)",
            16,
            nextY,
            initialMigrateLegacy,
            _textPrimary);
        _chkMigrateLegacy.BackColor = _bgCard;
        _chkMigrateLegacy.Visible = _showLegacyMigration;
        if (_showLegacyMigration) nextY += 26;

        _chkPurgeData = InstallerWizardControls.CreateDarkCheckBox(
            "Also delete user saves, settings.json, logs\\, and backups\\ (Purge all data)",
            16,
            48,
            initialPurgeData,
            AccentDanger);
        _chkPurgeData.BackColor = _bgCard;
        _chkPurgeData.Bold = true;
        _chkPurgeData.FontFamily = "Segoe UI Semibold";
        _chkPurgeData.FontSize = 9.25f;
        _chkPurgeData.Visible = _isUninstallMode;

        int minPanelH = showProfileRow ? 128 : 116;
        _optionsPanel.Height = _isUninstallMode ? 128 : Math.Max(minPanelH, nextY + 12);
        _optionsPanel.AddControl(_lblOptionsHeader);
        _optionsPanel.AddControl(_lblProfileHeader);
        _optionsPanel.AddControl(_rbProfileLightweight);
        _optionsPanel.AddControl(_rbProfileFull);
        _optionsPanel.AddControl(_rbProfileCustom);
        _optionsPanel.AddControl(_chkShortcuts);
        _optionsPanel.AddControl(_chkAutoStart);
        _optionsPanel.AddControl(_chkLaunchAfter);
        _optionsPanel.AddControl(_chkMigrateLegacy);
        _optionsPanel.AddControl(_chkPurgeData);

        bool isMultiLine = string.Equals(_layoutMode, "multi-line", StringComparison.OrdinalIgnoreCase);
        _lblStatus = new WizardLabel
        {
            Text = _isUninstallMode
                ? $"Ready to uninstall {_displayName}."
                : $"Ready to install or upgrade {_displayName}.",
            AutoSize = false,
            AutoEllipsis = !isMultiLine,
            Location = new WizardPoint(22, 294),
            Size = new WizardSize(606, isMultiLine ? 38 : 22),
            ForeColor = TextMuted,
            BackColor = _bgPrimary,
            FontFamily = "Segoe UI",
            FontSize = 9.0f
        };

        _progressBar = new WizardProgressBar
        {
            Location = new WizardPoint(22, 318),
            Size = new WizardSize(606, 18),
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };

        _btnToggleDetails = InstallerWizardControls.CreateFlatButton(
            "[>] Show Details",
            new WizardPoint(22, 342),
            new WizardSize(132, 26),
            _bgPrimary,
            _accentColor,
            null,
            bold: true);
        _btnToggleDetails.FontFamily = "Segoe UI Semibold";
        _btnToggleDetails.FontSize = 8.5f;
        _btnToggleDetails.TextAlignLeft = true;
        _btnToggleDetails.Click += (_, _) => SetDetailsExpanded(!_isDetailsExpanded);

        _txtLog = new WizardTextBox
        {
            Location = new WizardPoint(22, 372),
            Size = new WizardSize(606, 102),
            Multiline = true,
            ReadOnly = true,
            BackColor = BgInput,
            ForeColor = TextMuted,
            FontFamily = "Consolas",
            FontSize = 8.5f,
            Visible = false
        };

        _footerPanel = new WizardPanel
        {
            Location = new WizardPoint(0, CollapsedClientHeight - 56),
            Size = new WizardSize(DefaultClientWidth, 56),
            BackColor = _bgCard
        };

        _btnCancel = InstallerWizardControls.CreateFlatButton(
            _buttonOverrides?.CancelText ?? "Cancel",
            new WizardPoint(376, 11),
            new WizardSize(104, 34),
            _bgPrimary,
            _textPrimary,
            _borderSubtle,
            bold: false);
        _btnCancel.FontSize = 9.5f;
        _btnCancel.Click += OnCancelClicked;

        _btnPrimary = InstallerWizardControls.CreateFlatButton(
            _isUninstallMode
                ? (_buttonOverrides?.UninstallText ?? "Uninstall")
                : (_buttonOverrides?.InstallText ?? "Install Now"),
            new WizardPoint(490, 11),
            new WizardSize(138, 34),
            _isUninstallMode ? AccentDanger : _accentColor,
            WizardColor.White,
            null,
            bold: true);
        _btnPrimary.FontSize = 9.5f;
        _btnPrimary.Click += async (_, _) => await OnPrimaryActionClickedAsync();

        _footerPanel.AddControl(_btnCancel);
        _footerPanel.AddControl(_btnPrimary);

        InstallerWizardControls.ApplyTooltips(_toolTipMap, _tooltips, new Dictionary<string, WizardControl>(StringComparer.OrdinalIgnoreCase)
        {
            ["installDir"] = _txtInstallDir,
            ["secondaryDir"] = _txtSecondaryDir,
            ["profile"] = _lblProfileHeader,
            ["shortcuts"] = _chkShortcuts,
            ["autoStart"] = _chkAutoStart,
            ["launchAfter"] = _chkLaunchAfter,
            ["migrateLegacy"] = _chkMigrateLegacy,
            ["primaryButton"] = _btnPrimary,
            ["cancelButton"] = _btnCancel,
            ["detailsToggle"] = _btnToggleDetails
        });

        _allControls.AddRange(new WizardControl[]
        {
            _headerPanel, _lblDirHeader, _txtInstallDir, _btnBrowse,
            _lblSecondaryDirHeader, _txtSecondaryDir, _btnSecondaryBrowse,
            _optionsPanel, _lblStatus, _progressBar, _btnToggleDetails, _txtLog, _footerPanel
        });
    }

    public void ToggleDetailsDrawer() => SetDetailsExpanded(!_isDetailsExpanded);

    public void ToggleDetails() => SetDetailsExpanded(!_isDetailsExpanded);

    private int ComputeTargetClientHeight(bool expanded)
    {
        int extraStatus = string.Equals(_layoutMode, "multi-line", StringComparison.OrdinalIgnoreCase) ? 16 : 0;
        int baseBottom = (_hasSecondaryPicker ? 212 : 154) + _optionsPanel.Height + 12 + 78 + extraStatus;
        int collapsed = Math.Max(CollapsedClientHeight, baseBottom + 64);
        return expanded ? collapsed + 110 : collapsed;
    }

    public void SetDetailsExpanded(bool expanded)
    {
        _isDetailsExpanded = expanded;
        _txtLog.Visible = expanded;
        _btnToggleDetails.Text = expanded ? "[v] Hide Details" : "[>] Show Details";
        ClientSize = new WizardSize(
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

        int headerW = _headerPanel.Width > 0 ? _headerPanel.Width : clientW;
        int badgeWidth = Math.Max(40, _lblModeBadge.PreferredWidth > 0 ? _lblModeBadge.PreferredWidth : 128);
        _lblModeBadge.Size = new WizardSize(badgeWidth, 20);
        _lblModeBadge.Location = new WizardPoint(Math.Max(margin, headerW - margin - badgeWidth), 16);

        int titleWidth = Math.Max(40, _lblModeBadge.Left - 12 - _lblTitle.Left);
        _lblTitle.Size = new WizardSize(titleWidth, 28);

        int subWidth = Math.Max(40, _lblModeBadge.Left - 12 - _lblSub.Left);
        int subHeight = 20 * (string.Equals(_layoutMode, "multi-line", StringComparison.OrdinalIgnoreCase) ? 2 : 1);
        _lblSub.Size = new WizardSize(subWidth, subHeight);

        int contentW = Math.Max(200, clientW - (margin * 2));
        _btnBrowse.Location = new WizardPoint(clientW - margin - _btnBrowse.Width, 112);
        _txtInstallDir.Location = new WizardPoint(margin, 114);
        _txtInstallDir.Width = Math.Max(120, _btnBrowse.Left - 8 - margin);

        if (_hasSecondaryPicker)
        {
            _lblSecondaryDirHeader.Location = new WizardPoint(margin, 150);
            _btnSecondaryBrowse.Location = new WizardPoint(clientW - margin - _btnSecondaryBrowse.Width, 170);
            _txtSecondaryDir.Location = new WizardPoint(margin, 172);
            _txtSecondaryDir.Width = Math.Max(120, _btnSecondaryBrowse.Left - 8 - margin);
        }

        int optionsY = _hasSecondaryPicker ? 212 : 154;
        _optionsPanel.Location = new WizardPoint(margin, optionsY);
        _optionsPanel.Width = contentW;

        int statusOffset = string.Equals(_layoutMode, "multi-line", StringComparison.OrdinalIgnoreCase) ? 16 : 0;
        int belowOptionsY = optionsY + _optionsPanel.Height + 12;
        _lblStatus.Location = new WizardPoint(margin, belowOptionsY);
        _lblStatus.Width = contentW;

        _progressBar.Location = new WizardPoint(margin, belowOptionsY + 24 + statusOffset);
        _progressBar.Width = contentW;

        _btnToggleDetails.Location = new WizardPoint(margin, belowOptionsY + 48 + statusOffset);

        _txtLog.Location = new WizardPoint(margin, belowOptionsY + 78 + statusOffset);
        _txtLog.Size = new WizardSize(contentW, 102);

        int targetH = ComputeTargetClientHeight(_isDetailsExpanded);
        if (ClientSize.Height != targetH)
        {
            ClientSize = new WizardSize(clientW, targetH);
        }

        _footerPanel.Location = new WizardPoint(0, Math.Max(0, targetH - 56));
        _footerPanel.Size = new WizardSize(clientW, 56);
        int footerW = _footerPanel.Width > 0 ? _footerPanel.Width : clientW;
        int footerH = _footerPanel.Height > 0 ? _footerPanel.Height : 56;
        int btnY = Math.Max(6, (footerH - _btnPrimary.Height) / 2);

        _btnPrimary.Location = new WizardPoint(Math.Max(margin, footerW - margin - _btnPrimary.Width), btnY);
        _btnCancel.Location = new WizardPoint(Math.Max(margin, _btnPrimary.Left - 10 - _btnCancel.Width), btnY);

        SyncAllNativeControlBounds();
    }
}
