// @module: Foundation.Installer.UI.Wizard
// @purpose: Reusable 650x460 (collapsed) / 650x570 (expanded) Pure Win32 NativeAOT Dark Setup/Upgrade/Uninstall Wizard supporting both InstallerEngine and InstallerEngineCore.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Reflection, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: InstallerWizardForm

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;

namespace Foundation.Installer;

public sealed partial class InstallerWizardForm : IDisposable
{
    public const int DefaultClientWidth = 650;
    public const int CollapsedClientHeight = 434;
    public const int ExpandedClientHeight = 544;

    private readonly InstallerEngine? _fullEngine;
    private readonly InstallerRuntimeContext? _ctx;
    private readonly InstallerEngineCore? _coreEngine;
    private readonly Func<CancellationToken, Action<int, string>, Task<FoundationInstallResult>>? _customInstallAction;
    private readonly string? _externalPayloadPath;
    private readonly string? _offlineBundlePath;
    private readonly string _deliveryLabel;
    private readonly bool _isUninstallMode;
    private readonly string _displayName;
    private readonly string _version;
    private readonly string _subtitle;
    private readonly WizardColor _accentColor;
    private readonly WizardColor _bgPrimary;
    private readonly WizardColor _bgCard;
    private readonly bool _allowAutoStart;
    private readonly SecondaryPathPickerOptions? _secondaryPickerOptions;
    private readonly bool _hasSecondaryPicker;
    private readonly bool _showLegacyMigration;
    private readonly string? _legacyDetectedPath;
    private readonly string _layoutMode;
    private readonly IReadOnlyList<string> _supportedProfiles;
    private readonly string? _iconPath;
    private readonly WizardColor _textPrimary;
    private readonly WizardColor _borderSubtle;
    private readonly InstallerMarkupButtons? _buttonOverrides;
    private readonly IReadOnlyDictionary<string, InstallerMarkupTooltip>? _tooltips;
    private readonly Assembly _payloadAssembly;

    private WizardPanel _headerPanel = null!;
    private WizardPanel _optionsPanel = null!;
    private WizardPanel _footerPanel = null!;
    private WizardLabel _lblTitle = null!;
    private WizardLabel _lblSub = null!;
    private WizardLabel _lblDirHeader = null!;
    private WizardTextBox _txtInstallDir = null!;
    private WizardButton _btnBrowse = null!;
    private WizardLabel _lblSecondaryDirHeader = null!;
    private WizardTextBox _txtSecondaryDir = null!;
    private WizardButton _btnSecondaryBrowse = null!;
    private WizardLabel _lblOptionsHeader = null!;
    private WizardLabel _lblProfileHeader = null!;
    private WizardRadioButton _rbProfileLightweight = null!;
    private WizardRadioButton _rbProfileFull = null!;
    private WizardRadioButton _rbProfileCustom = null!;
    private WizardCheckBox _chkShortcuts = null!;
    private WizardCheckBox _chkAutoStart = null!;
    private WizardCheckBox _chkLaunchAfter = null!;
    private WizardCheckBox _chkMigrateLegacy = null!;
    private WizardCheckBox _chkPurgeData = null!;
    private WizardLabel _lblModeBadge = null!;
    private WizardLabel _lblStatus = null!;
    private WizardProgressBar _progressBar = null!;
    private WizardButton _btnToggleDetails = null!;
    private WizardTextBox _txtLog = null!;
    private WizardButton _btnPrimary = null!;
    private WizardButton _btnCancel = null!;

    private readonly Dictionary<WizardControl, string> _toolTipMap = new();
    private readonly List<WizardControl> _allControls = new();
    private readonly ConcurrentQueue<Action> _uiQueue = new();

    private CancellationTokenSource? _cts;
    private WizardSize _clientSize = new(DefaultClientWidth, CollapsedClientHeight);
    private string _windowText = string.Empty;
    private bool _controlsInitialized;
    private bool _completedSuccessfully;
    private bool _isDetailsExpanded;
    private bool _disposed;
    private string _completedInstallDir = string.Empty;

    private static readonly WizardColor BgInput = InstallerWizardControls.BgInput;
    private static readonly WizardColor BorderSubtle = InstallerWizardControls.BorderSubtle;
    private static readonly WizardColor TextPrimary = InstallerWizardControls.TextPrimary;
    private static readonly WizardColor TextMuted = InstallerWizardControls.TextMuted;
    private static readonly WizardColor AccentEmerald = InstallerWizardControls.AccentEmerald;
    private static readonly WizardColor AccentDanger = InstallerWizardControls.AccentDanger;

    public int ExitCode { get; private set; } = 1;
    public string Text { get => _windowText; set => SetNativeWindowTitle(value); }
    public WizardSize ClientSize { get => _clientSize; set => SetNativeClientSize(value); }
    public IReadOnlyList<WizardControl> Controls => _allControls;
    public WizardPanel HeaderPanel => _headerPanel;
    public WizardPanel FooterPanel => _footerPanel;
    public WizardPanel OptionsPanel => _optionsPanel;
    public WizardButton PrimaryButton => _btnPrimary;
    public WizardButton CancelButton => _btnCancel;
    public WizardButton BrowseButton => _btnBrowse;
    public WizardButton DetailsToggleButton => _btnToggleDetails;
    public WizardLabel ModeBadgeLabel => _lblModeBadge;
    public WizardLabel TitleLabel => _lblTitle;
    public WizardLabel SubtitleLabel => _lblSub;
    public WizardTextBox InstallDirTextBox => _txtInstallDir;
    public WizardTextBox LogTextBox => _txtLog;
    public WizardProgressBar SetupProgressBar => _progressBar;
    public WizardLabel StatusLabel => _lblStatus;
    public WizardCheckBox ShortcutsCheckBox => _chkShortcuts;
    public WizardCheckBox AutoStartCheckBox => _chkAutoStart;
    public WizardCheckBox LaunchAfterCheckBox => _chkLaunchAfter;
    public WizardCheckBox MigrateLegacyCheckBox => _chkMigrateLegacy;
    public WizardRadioButton LightweightProfileRadioButton => _rbProfileLightweight;
    public WizardRadioButton FullProfileRadioButton => _rbProfileFull;
    public WizardRadioButton CustomProfileRadioButton => _rbProfileCustom;
    public WizardTextBox SecondaryDirTextBox => _txtSecondaryDir;
    public string LayoutMode => _layoutMode;
    public IReadOnlyList<string> SupportedProfiles => _supportedProfiles;
    public string? GetControlTooltip(WizardControl control) => _toolTipMap.TryGetValue(control, out string? tip) ? tip : null;
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
        Assembly? payloadAssembly = null,
        string? offlineBundlePath = null)
    {
        _fullEngine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _payloadAssembly = payloadAssembly ?? _fullEngine.PayloadAssembly;
        _externalPayloadPath = externalPayloadPath;
        _offlineBundlePath = offlineBundlePath;
        _isUninstallMode = isUninstallMode;

        InstallerAppDescriptor desc = _fullEngine.Descriptor;
        _deliveryLabel = desc.IsOnlineSetup ? "ONLINE SETUP" : "OFFLINE SETUP";
        _displayName = desc.DisplayName;
        _version = desc.Version;
        _subtitle = desc.EffectiveSubtitle;
        _allowAutoStart = desc.AllowAutoStart;
        _accentColor = InstallerWizardControls.ParseHexColor(desc.BrandAccentHex, WizardColor.FromRgb(14, 165, 233));
        _bgPrimary = InstallerWizardControls.ParseHexColor(desc.SurfaceBackdropHex, WizardColor.FromRgb(14, 17, 23));
        _bgCard = InstallerWizardControls.ParseHexColor(desc.SurfaceCardHex, WizardColor.FromRgb(22, 27, 36));
        _textPrimary = InstallerWizardControls.ParseHexColor(desc.TextPrimaryHex, TextPrimary);
        _borderSubtle = InstallerWizardControls.ParseHexColor(desc.BorderHex, BorderSubtle);
        _layoutMode = InstallerManifestStore.NormalizeLayoutMode(desc.LayoutMode);
        _supportedProfiles = InstallerManifestStore.NormalizeSupportedProfiles(desc.SupportedProfiles);
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
        _deliveryLabel = string.Equals(theme.InstallerDeliveryKind, InstallerDeliveryKinds.OnlineSetup, StringComparison.OrdinalIgnoreCase)
            ? "ONLINE SETUP"
            : "OFFLINE SETUP";
        _displayName = theme.DisplayName;
        _version = theme.Version;
        _subtitle = theme.Subtitle;
        _allowAutoStart = theme.AllowAutoStart;
        _accentColor = InstallerWizardControls.ParseHexColor(theme.BrandAccentHex, WizardColor.FromRgb(14, 165, 233));
        _bgPrimary = InstallerWizardControls.ParseHexColor(theme.SurfaceBackdropHex, WizardColor.FromRgb(14, 17, 23));
        _bgCard = InstallerWizardControls.ParseHexColor(theme.SurfaceCardHex, WizardColor.FromRgb(22, 27, 36));
        _textPrimary = InstallerWizardControls.ParseHexColor(theme.TextPrimaryHex, TextPrimary);
        _borderSubtle = InstallerWizardControls.ParseHexColor(theme.BorderHex, BorderSubtle);
        _layoutMode = InstallerManifestStore.NormalizeLayoutMode(theme.LayoutMode);
        _supportedProfiles = InstallerManifestStore.NormalizeSupportedProfiles(
            theme.SupportedProfiles ?? (theme.AvailableProfiles.Count > 0 ? theme.AvailableProfiles.Select(p => p.Id) : null));
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

    private void InitializeHeaderAndDestinationControls(string defaultDir, string? initialSecondaryDir)
    {
        bool isMultiLine = string.Equals(_layoutMode, "multi-line", StringComparison.OrdinalIgnoreCase);
        _headerPanel = new WizardPanel
        {
            Location = new WizardPoint(0, 0),
            Size = new WizardSize(DefaultClientWidth, isMultiLine ? 90 : 76),
            BackColor = _bgCard
        };

        int headerTextLeft = 22;
        int titleHeight = 28;
        _lblTitle = new WizardLabel
        {
            Text = _isUninstallMode
                ? $"{_displayName} — Remove Installation"
                : $"{_displayName} — Setup Wizard",
            FontFamily = "Segoe UI Semibold",
            FontSize = 13.0f,
            Bold = true,
            ForeColor = _textPrimary,
            BackColor = _bgCard,
            AutoSize = false,
            AutoEllipsis = true,
            Location = new WizardPoint(headerTextLeft, 13),
            Size = new WizardSize(DefaultClientWidth - headerTextLeft - 150, titleHeight)
        };

        int subHeight = isMultiLine ? 40 : 20;
        _lblSub = new WizardLabel
        {
            Text = _isUninstallMode
                ? $"Version {_version}  •  Preserves data\\, saves\\, and logs\\ unless purged below"
                : $"Version {_version}  •  {_deliveryLabel}  •  {_subtitle}",
            FontFamily = "Segoe UI",
            FontSize = 8.75f,
            ForeColor = TextMuted,
            BackColor = _bgCard,
            AutoSize = false,
            AutoEllipsis = !isMultiLine,
            Location = new WizardPoint(headerTextLeft, 43),
            Size = new WizardSize(DefaultClientWidth - headerTextLeft - 150, subHeight)
        };

        _lblModeBadge = new WizardLabel
        {
            AutoSize = true,
            FontFamily = "Segoe UI Semibold",
            FontSize = 8.5f,
            Bold = true,
            ForeColor = _accentColor,
            BackColor = _bgCard,
            Location = new WizardPoint(495, 16),
            Size = new WizardSize(128, 20)
        };

        _headerPanel.AddControl(_lblTitle);
        _headerPanel.AddControl(_lblSub);
        _headerPanel.AddControl(_lblModeBadge);

        _lblDirHeader = InstallerWizardControls.CreateSectionLabel(
            _isUninstallMode ? "Installed Location:" : "Destination Folder:",
            new WizardPoint(22, 90),
            _textPrimary);
        _lblDirHeader.BackColor = _bgPrimary;

        _txtInstallDir = InstallerWizardControls.CreateDarkPathTextBox(defaultDir, new WizardPoint(22, 114), 502);
        _txtInstallDir.TextChanged += (_, _) => UpdateUpgradeDetectionBadge();

        _btnBrowse = InstallerWizardControls.CreateFlatButton(
            "Change...",
            new WizardPoint(532, 112),
            new WizardSize(96, 29),
            _bgCard,
            _textPrimary,
            _borderSubtle);
        _btnBrowse.Click += OnBrowseClicked;

        InitializeSecondaryDestinationControls(initialSecondaryDir);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Dispose();
        _cts = null;
        DisposeNativeHandles();
    }
}
