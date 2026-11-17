// @module: Foundation.Installer.UI.Wizard
// @purpose: Reusable 650x460 (collapsed) / 650x570 (expanded) Dark WinForms Setup/Upgrade/Uninstall Wizard supporting both InstallerEngine and InstallerEngineCore.
// @depends-on: System, System.Drawing, System.IO, System.Threading, System.Threading.Tasks, System.Windows.Forms, Foundation.Core
// @exports: InstallerWizardForm (partial: constructors, properties, header/destination controls)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Foundation.Core;

namespace Foundation.Installer;

public sealed partial class InstallerWizardForm : Form
{
    public const int DefaultClientWidth = 650;
    public const int CollapsedClientHeight = 434;
    public const int ExpandedClientHeight = 544;

    private readonly InstallerEngine? _fullEngine;
    private readonly InstallerRuntimeContext? _ctx;
    private readonly InstallerEngineCore? _coreEngine;
    private readonly Func<CancellationToken, Action<int, string>, Task<FoundationInstallResult>>? _customInstallAction;
    private readonly string? _externalPayloadPath;
    private readonly bool _isUninstallMode;
    private readonly string _displayName;
    private readonly string _version;
    private readonly string _subtitle;
    private readonly Color _accentColor;
    private readonly Color _bgPrimary;
    private readonly Color _bgCard;
    private readonly bool _allowAutoStart;
    private readonly SecondaryPathPickerOptions? _secondaryPickerOptions;
    private readonly bool _hasSecondaryPicker;
    private readonly bool _showLegacyMigration;
    private readonly string? _legacyDetectedPath;
    private readonly string _layoutMode;
    private readonly IReadOnlyList<string> _supportedProfiles;
    private readonly string? _logoPath;
    private readonly string? _iconPath;
    private readonly Color _textPrimary;
    private readonly Color _borderSubtle;
    private readonly InstallerMarkupButtons? _buttonOverrides;
    private readonly IReadOnlyDictionary<string, InstallerMarkupTooltip>? _tooltips;
    private readonly Assembly _payloadAssembly;

    private Panel _headerPanel = null!;
    private Panel _bodyPanel = null!;
    private Panel _footerPanel = null!;
    private Panel _optionsPanel = null!;
    private PictureBox _picAppIcon = null!;
    private Label _lblTitle = null!;
    private Label _lblSub = null!;
    private Label _lblDirHeader = null!;
    private TextBox _txtInstallDir = null!;
    private Button _btnBrowse = null!;
    private Label _lblSecondaryDirHeader = null!;
    private TextBox _txtSecondaryDir = null!;
    private Button _btnSecondaryBrowse = null!;
    private Label _lblProfileHeader = null!;
    private RadioButton _rbProfileLightweight = null!;
    private RadioButton _rbProfileFull = null!;
    private RadioButton _rbProfileCustom = null!;
    private ToolTip _wizardToolTip = null!;
    private CheckBox _chkShortcuts = null!;
    private CheckBox _chkAutoStart = null!;
    private CheckBox _chkLaunchAfter = null!;
    private CheckBox _chkMigrateLegacy = null!;
    private CheckBox _chkPurgeData = null!;
    private Label _lblModeBadge = null!;
    private Label _lblStatus = null!;
    private ProgressBar _progressBar = null!;
    private Button _btnToggleDetails = null!;
    private TextBox _txtLog = null!;
    private Button _btnPrimary = null!;
    private Button _btnCancel = null!;

    private CancellationTokenSource? _cts;
    private bool _controlsInitialized;
    private bool _completedSuccessfully;
    private bool _isDetailsExpanded;
    private string _completedInstallDir = string.Empty;

    private static readonly Color BgInput = InstallerWizardControls.BgInput;
    private static readonly Color BorderSubtle = InstallerWizardControls.BorderSubtle;
    private static readonly Color TextPrimary = InstallerWizardControls.TextPrimary;
    private static readonly Color TextMuted = InstallerWizardControls.TextMuted;
    private static readonly Color AccentEmerald = InstallerWizardControls.AccentEmerald;
    private static readonly Color AccentDanger = InstallerWizardControls.AccentDanger;

    public int ExitCode { get; private set; } = 1;
    public Panel HeaderPanel => _headerPanel;
    public Panel FooterPanel => _footerPanel;
    public Panel OptionsPanel => _optionsPanel;
    public Button PrimaryButton => _btnPrimary;
    public new Button CancelButton => _btnCancel;
    public Button BrowseButton => _btnBrowse;
    public Button DetailsToggleButton => _btnToggleDetails;
    public Label ModeBadgeLabel => _lblModeBadge;
    public Label TitleLabel => _lblTitle;
    public Label SubtitleLabel => _lblSub;
    public TextBox InstallDirTextBox => _txtInstallDir;
    public TextBox LogTextBox => _txtLog;
    public ProgressBar SetupProgressBar => _progressBar;
    public Label StatusLabel => _lblStatus;
    public CheckBox ShortcutsCheckBox => _chkShortcuts;
    public CheckBox AutoStartCheckBox => _chkAutoStart;
    public CheckBox LaunchAfterCheckBox => _chkLaunchAfter;
    public CheckBox MigrateLegacyCheckBox => _chkMigrateLegacy;
    public RadioButton LightweightProfileRadioButton => _rbProfileLightweight;
    public RadioButton FullProfileRadioButton => _rbProfileFull;
    public RadioButton CustomProfileRadioButton => _rbProfileCustom;
    public TextBox SecondaryDirTextBox => _txtSecondaryDir;
    public string LayoutMode => _layoutMode;
    public IReadOnlyList<string> SupportedProfiles => _supportedProfiles;
    public string? GetControlTooltip(Control control) => _wizardToolTip?.GetToolTip(control);
    public string SelectedProfileId =>
        _rbProfileCustom.Checked ? "custom"
        : (_rbProfileFull.Checked ? "full"
        : (_rbProfileLightweight.Checked ? "lightweight" : (_supportedProfiles.FirstOrDefault() ?? "lightweight")));
    public string SecondaryInstallDir => _hasSecondaryPicker ? _txtSecondaryDir.Text.Trim() : string.Empty;
    public bool MigrateLegacyChecked => _showLegacyMigration && _chkMigrateLegacy.Checked;
    public bool IsAutoStartAllowed => _allowAutoStart;
    public bool IsDetailsExpanded => _isDetailsExpanded;

    public InstallerWizardForm(
        InstallerEngine engine,
        InstallerRuntimeContext ctx,
        string? initialInstallDir = null,
        string? externalPayloadPath = null,
        bool isUninstallMode = false,
        bool initialCreateShortcuts = true,
        bool initialAutoStart = false,
        bool initialLaunchAfter = true,
        bool initialPurgeData = false,
        string? initialProfileId = null,
        string? initialSecondaryDir = null,
        string? secondaryFlagName = null,
        bool initialMigrateLegacy = false,
        bool? showLegacyMigration = null,
        Assembly? payloadAssembly = null)
    {
        _fullEngine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _payloadAssembly = payloadAssembly ?? _fullEngine.PayloadAssembly;
        _externalPayloadPath = externalPayloadPath;
        _isUninstallMode = isUninstallMode;

        InstallerAppDescriptor desc = _fullEngine.Descriptor;
        _displayName = desc.DisplayName;
        _version = desc.Version;
        _subtitle = desc.EffectiveSubtitle;
        _allowAutoStart = desc.AllowAutoStart;
        _accentColor = InstallerWizardControls.ParseHexColor(desc.BrandAccentHex, Color.FromArgb(14, 165, 233));
        _bgPrimary = InstallerWizardControls.ParseHexColor(desc.SurfaceBackdropHex, Color.FromArgb(14, 17, 23));
        _bgCard = InstallerWizardControls.ParseHexColor(desc.SurfaceCardHex, Color.FromArgb(22, 27, 36));
        _textPrimary = InstallerWizardControls.ParseHexColor(desc.TextPrimaryHex, TextPrimary);
        _borderSubtle = InstallerWizardControls.ParseHexColor(desc.BorderHex, BorderSubtle);
        _layoutMode = InstallerManifestStore.NormalizeLayoutMode(desc.LayoutMode);
        _supportedProfiles = InstallerManifestStore.NormalizeSupportedProfiles(desc.SupportedProfiles);
        _logoPath = desc.LogoPath;
        _iconPath = desc.IconPath;
        _buttonOverrides = desc.Buttons;
        _tooltips = desc.Tooltips;

        string effectiveProfile = initialProfileId ?? ctx.ConfigOverride?.InstallProfile ?? desc.DefaultProfile;
        string? effectiveSecondary = initialSecondaryDir
            ?? ctx.ConfigOverride?.ReEngineInstallDir
            ?? ctx.ConfigOverride?.SuiteInstallDir;
        if (!string.IsNullOrWhiteSpace(effectiveSecondary))
        {
            string flag = secondaryFlagName ?? (string.IsNullOrWhiteSpace(ctx.ConfigOverride?.ReEngineInstallDir) ? "--suite-dir" : "--reengine-dir");
            string label = flag.Equals("--reengine-dir", StringComparison.OrdinalIgnoreCase)
                ? "Companion reEngine Directory:"
                : "Companion Suite Directory:";
            _secondaryPickerOptions = new SecondaryPathPickerOptions(flag, label, effectiveSecondary);
        }
        else if (desc.SecondaryPickers is { Count: > 0 })
        {
            _secondaryPickerOptions = desc.SecondaryPickers[0];
            effectiveSecondary = _secondaryPickerOptions.DefaultPath;
        }
        if (string.IsNullOrWhiteSpace(initialSecondaryDir) && !string.IsNullOrWhiteSpace(effectiveSecondary) && ctx.IsSandboxActive)
        {
            effectiveSecondary = KnownFolderPaths.MapHostPathToSandbox(effectiveSecondary, ctx.Sandbox);
        }
        _hasSecondaryPicker = !_isUninstallMode && _secondaryPickerOptions != null;
        _showLegacyMigration = !_isUninstallMode && (showLegacyMigration ?? (initialMigrateLegacy || (ctx.ConfigOverride?.MigrateLegacy ?? false) || desc.AllowLegacyMigration));

        string defaultDir = !string.IsNullOrWhiteSpace(initialInstallDir)
            ? initialInstallDir
            : _fullEngine.ResolveDefaultInstallDirectory();

        InitializeWizardSurface(
            defaultDir,
            initialCreateShortcuts,
            _allowAutoStart && initialAutoStart,
            initialLaunchAfter,
            initialPurgeData,
            effectiveProfile,
            effectiveSecondary,
            initialMigrateLegacy);
    }

    public InstallerWizardForm(
        InstallerEngineCore engine,
        bool isUninstallMode = false,
        string? initialInstallDir = null,
        Func<CancellationToken, Action<int, string>, Task<FoundationInstallResult>>? customInstallAction = null,
        string? initialProfileId = null,
        string? initialSecondaryDir = null,
        bool initialMigrateLegacy = false,
        bool? showLegacyMigration = null,
        Assembly? payloadAssembly = null)
    {
        _coreEngine = engine ?? throw new ArgumentNullException(nameof(engine));
        _payloadAssembly = payloadAssembly ?? Assembly.GetEntryAssembly() ?? typeof(InstallerWizardForm).Assembly;
        _isUninstallMode = isUninstallMode;
        _customInstallAction = customInstallAction;

        InstallerThemeOptions theme = _coreEngine.Theme;
        _displayName = theme.DisplayName;
        _version = theme.Version;
        _subtitle = theme.Subtitle;
        _allowAutoStart = theme.AllowAutoStart;
        _accentColor = InstallerWizardControls.ParseHexColor(theme.BrandAccentHex, Color.FromArgb(14, 165, 233));
        _bgPrimary = InstallerWizardControls.ParseHexColor(theme.SurfaceBackdropHex, Color.FromArgb(14, 17, 23));
        _bgCard = InstallerWizardControls.ParseHexColor(theme.SurfaceCardHex, Color.FromArgb(22, 27, 36));
        _textPrimary = InstallerWizardControls.ParseHexColor(theme.TextPrimaryHex, TextPrimary);
        _borderSubtle = InstallerWizardControls.ParseHexColor(theme.BorderHex, BorderSubtle);
        _layoutMode = InstallerManifestStore.NormalizeLayoutMode(theme.LayoutMode);
        _supportedProfiles = InstallerManifestStore.NormalizeSupportedProfiles(
            theme.SupportedProfiles ?? (theme.AvailableProfiles.Count > 0 ? theme.AvailableProfiles.Select(p => p.Id) : null));
        _logoPath = theme.HeaderLogoResource;
        _iconPath = theme.WindowIconResource;
        _buttonOverrides = theme.Buttons;
        _tooltips = theme.Tooltips;

        _secondaryPickerOptions = theme.SecondaryPathPicker ?? theme.SecondaryPickers?.FirstOrDefault();
        string? effectiveSecondary = !string.IsNullOrWhiteSpace(initialSecondaryDir)
            ? initialSecondaryDir
            : _secondaryPickerOptions?.DefaultPath;
        if (string.IsNullOrWhiteSpace(initialSecondaryDir) &&
            !string.IsNullOrWhiteSpace(effectiveSecondary) &&
            _coreEngine.IsSandboxActive)
        {
            effectiveSecondary = KnownFolderPaths.MapHostPathToSandbox(effectiveSecondary, _coreEngine.Sandbox);
        }
        _hasSecondaryPicker = !_isUninstallMode && (_secondaryPickerOptions != null || !string.IsNullOrWhiteSpace(effectiveSecondary));
        _legacyDetectedPath = !_isUninstallMode ? _coreEngine.DetectLegacyFlatInstall() : null;
        _showLegacyMigration = !_isUninstallMode && (showLegacyMigration ?? (theme.AllowLegacyMigration || initialMigrateLegacy || _legacyDetectedPath != null));

        string defaultDir = _coreEngine.ResolveTargetInstallDir(initialInstallDir);
        string effectiveProfile = initialProfileId ?? theme.DefaultProfileId;

        InitializeWizardSurface(
            defaultDir,
            initialCreateShortcuts: true,
            initialAutoStart: false,
            initialLaunchAfter: true,
            initialPurgeData: false,
            effectiveProfile,
            effectiveSecondary,
            initialMigrateLegacy);
    }

    private void InitializeHeaderAndDestinationControls(string defaultDir, string? initialSecondaryDir, Icon? extractedIcon)
    {
        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Size = new Size(DefaultClientWidth, 76),
            BackColor = _bgCard,
            Padding = new Padding(22, 14, 22, 12)
        };

        Image? logoImage = InstallerWizardControls.TryLoadImage(_logoPath, _payloadAssembly)
            ?? extractedIcon?.ToBitmap();
        _picAppIcon = new PictureBox
        {
            Size = new Size(36, 36),
            Location = new Point(22, 20),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Visible = logoImage != null,
            Image = logoImage
        };

        int headerTextLeft = logoImage != null ? 68 : 22;
        bool isMultiLine = string.Equals(_layoutMode, "multi-line", StringComparison.OrdinalIgnoreCase);
        if (isMultiLine) _headerPanel.Height = 90;

        var titleFont = new Font("Segoe UI Semibold", 13.0f, FontStyle.Bold);
        int titleHeight = Math.Max(28, TextRenderer.MeasureText("Mg", titleFont).Height + 4);
        _lblTitle = new Label
        {
            Text = _isUninstallMode
                ? $"{_displayName} — Remove Installation"
                : $"{_displayName} — Setup Wizard",
            Font = titleFont,
            ForeColor = _textPrimary,
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(headerTextLeft, 13),
            Size = new Size(DefaultClientWidth - headerTextLeft - 150, titleHeight)
        };

        var subFont = new Font("Segoe UI", 8.75f, FontStyle.Regular);
        int baseSubHeight = Math.Max(20, TextRenderer.MeasureText("Mg", subFont).Height + 4);
        int subHeight = isMultiLine ? baseSubHeight * 2 : baseSubHeight;
        _lblSub = new Label
        {
            Text = _isUninstallMode
                ? $"Version {_version}  •  Preserves data\\, saves\\, and logs\\ unless purged below"
                : $"Version {_version}  •  {_subtitle}",
            Font = subFont,
            ForeColor = TextMuted,
            AutoSize = false,
            AutoEllipsis = !isMultiLine,
            Location = new Point(headerTextLeft, 43),
            Size = new Size(DefaultClientWidth - headerTextLeft - 150, subHeight)
        };

        _lblModeBadge = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
            ForeColor = _accentColor,
            Location = new Point(495, 16),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        _headerPanel.Controls.Add(_picAppIcon);
        _headerPanel.Controls.Add(_lblTitle);
        _headerPanel.Controls.Add(_lblSub);
        _headerPanel.Controls.Add(_lblModeBadge);
        _headerPanel.Resize += (_, _) => UpdateLayoutBounds();

        _bodyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 16, 22, 12),
            BackColor = _bgPrimary
        };

        _lblDirHeader = new Label
        {
            Text = _isUninstallMode ? "Installed Location:" : "Destination Folder:",
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            ForeColor = _textPrimary,
            AutoSize = true,
            Location = new Point(22, 90)
        };

        _txtInstallDir = new TextBox
        {
            Text = defaultDir,
            Location = new Point(22, 114),
            Width = 502,
            Height = 29,
            BackColor = BgInput,
            ForeColor = _textPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9.5f, FontStyle.Regular),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _txtInstallDir.TextChanged += (_, _) => UpdateUpgradeDetectionBadge();

        _btnBrowse = new Button
        {
            Text = "Change...",
            Location = new Point(532, 112),
            Size = new Size(96, 29),
            FlatStyle = FlatStyle.Flat,
            BackColor = _bgCard,
            ForeColor = _textPrimary,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _btnBrowse.FlatAppearance.BorderColor = _borderSubtle;
        _btnBrowse.Click += OnBrowseClicked;

        InitializeSecondaryDestinationControls(initialSecondaryDir);
    }
}
