// @module: Foundation.Installer.UI.Wizard
// @purpose: Reusable 650x434 (collapsed) / 650x544 (expanded) Dark WinForms Setup/Upgrade/Uninstall Wizard supporting both InstallerEngine and InstallerEngineCore.
// @depends-on: System, System.Drawing, System.IO, System.Runtime.InteropServices, System.Threading, System.Threading.Tasks, System.Windows.Forms
// @exports: InstallerWizardForm (partial: constructors, properties, header/destination controls)

using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Foundation.Installer;

public sealed partial class InstallerWizardForm : Form
{
    public const int DefaultClientWidth = 650;
    public const int CollapsedClientHeight = 434;
    public const int ExpandedClientHeight = 544;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private readonly InstallerEngine? _fullEngine;
    private readonly InstallerSandboxContext? _ctx;
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
    private readonly int _showcaseAutoCloseMs;
    private System.Windows.Forms.Timer? _showcaseTimer;

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
    private CheckBox _chkShortcuts = null!;
    private CheckBox _chkAutoStart = null!;
    private CheckBox _chkLaunchAfter = null!;
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
    public TextBox InstallDirTextBox => _txtInstallDir;
    public TextBox LogTextBox => _txtLog;
    public ProgressBar SetupProgressBar => _progressBar;
    public Label StatusLabel => _lblStatus;
    public CheckBox ShortcutsCheckBox => _chkShortcuts;
    public CheckBox AutoStartCheckBox => _chkAutoStart;
    public CheckBox LaunchAfterCheckBox => _chkLaunchAfter;
    public bool IsAutoStartAllowed => _allowAutoStart;
    public bool IsDetailsExpanded => _isDetailsExpanded;

    public InstallerWizardForm(
        InstallerEngine engine,
        InstallerSandboxContext ctx,
        string? initialInstallDir = null,
        string? externalPayloadPath = null,
        bool isUninstallMode = false,
        bool initialCreateShortcuts = true,
        bool initialAutoStart = false,
        bool initialLaunchAfter = true,
        bool initialPurgeData = false,
        int showShowcaseAutoCloseMs = 0)
    {
        _fullEngine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _externalPayloadPath = externalPayloadPath;
        _isUninstallMode = isUninstallMode;

        InstallerAppDescriptor desc = _fullEngine.Descriptor;
        _displayName = desc.DisplayName;
        _version = desc.Version;
        _subtitle = desc.EffectiveSubtitle;
        _allowAutoStart = desc.AllowAutoStart;
        _showcaseAutoCloseMs = Math.Max(0, showShowcaseAutoCloseMs);
        _accentColor = InstallerWizardControls.ParseHexColor(desc.BrandAccentHex, Color.FromArgb(14, 165, 233));
        _bgPrimary = Color.FromArgb(14, 17, 23);
        _bgCard = Color.FromArgb(22, 27, 36);

        string defaultDir = !string.IsNullOrWhiteSpace(initialInstallDir)
            ? initialInstallDir
            : _fullEngine.ResolveDefaultInstallDirectory();

        InitializeWizardSurface(
            defaultDir,
            initialCreateShortcuts,
            _allowAutoStart && initialAutoStart,
            initialLaunchAfter,
            initialPurgeData);
    }

    public InstallerWizardForm(
        InstallerEngineCore engine,
        bool isUninstallMode = false,
        string? initialInstallDir = null,
        Func<CancellationToken, Action<int, string>, Task<FoundationInstallResult>>? customInstallAction = null,
        int showShowcaseAutoCloseMs = 0)
    {
        _coreEngine = engine ?? throw new ArgumentNullException(nameof(engine));
        _isUninstallMode = isUninstallMode;
        _customInstallAction = customInstallAction;

        InstallerThemeOptions theme = _coreEngine.Theme;
        _displayName = theme.DisplayName;
        _version = theme.Version;
        _subtitle = theme.Subtitle;
        _allowAutoStart = theme.AllowAutoStart;
        _showcaseAutoCloseMs = Math.Max(0, showShowcaseAutoCloseMs);
        _accentColor = InstallerWizardControls.ParseHexColor(theme.BrandAccentHex, Color.FromArgb(14, 165, 233));
        _bgPrimary = InstallerWizardControls.ParseHexColor(theme.SurfaceBackdropHex, Color.FromArgb(14, 17, 23));
        _bgCard = InstallerWizardControls.ParseHexColor(theme.SurfaceCardHex, Color.FromArgb(22, 27, 36));

        string defaultDir = _coreEngine.ResolveTargetInstallDir(initialInstallDir);

        InitializeWizardSurface(
            defaultDir,
            initialCreateShortcuts: true,
            initialAutoStart: false,
            initialLaunchAfter: true,
            initialPurgeData: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _showcaseTimer?.Stop();
            _showcaseTimer?.Dispose();
            _showcaseTimer = null;
            _cts?.Dispose();
            _cts = null;
        }
        base.Dispose(disposing);
    }

    private void InitializeWizardSurface(
        string defaultDir,
        bool initialCreateShortcuts,
        bool initialAutoStart,
        bool initialLaunchAfter,
        bool initialPurgeData)
    {
        Text = _isUninstallMode
            ? $"{_displayName} Uninstall (v{_version})"
            : $"{_displayName} Setup (v{_version})";
        ClientSize = new Size(DefaultClientWidth, CollapsedClientHeight);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = _bgPrimary;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        Icon? extractedIcon = null;
        try
        {
            string? selfExe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(selfExe) && File.Exists(selfExe))
            {
                extractedIcon = Icon.ExtractAssociatedIcon(selfExe);
                if (extractedIcon != null)
                {
                    Icon = extractedIcon;
                }
            }
        }
        catch { }

        InitializeHeaderAndDestinationControls(defaultDir, extractedIcon);
        InitializeOptionsAndFooterControls(
            initialCreateShortcuts,
            initialAutoStart,
            initialLaunchAfter,
            initialPurgeData);

        _controlsInitialized = true;
        UpdateUpgradeDetectionBadge();
        UpdateLayoutBounds();

        AppendLogLine($"{_displayName} Setup v{_version} initialized.");
        AppendLogLine($"Target directory: {_txtInstallDir.Text}");
    }

    private void InitializeHeaderAndDestinationControls(string defaultDir, Icon? extractedIcon)
    {
        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Size = new Size(DefaultClientWidth, 76),
            BackColor = _bgCard,
            Padding = new Padding(22, 14, 22, 12)
        };

        _picAppIcon = new PictureBox
        {
            Size = new Size(36, 36),
            Location = new Point(22, 20),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Visible = extractedIcon != null
        };
        if (extractedIcon != null)
        {
            _picAppIcon.Image = extractedIcon.ToBitmap();
        }

        int headerTextLeft = extractedIcon != null ? 68 : 22;

        _lblTitle = new Label
        {
            Text = _isUninstallMode
                ? $"{_displayName} — Remove Installation"
                : $"{_displayName} — Setup Wizard",
            Font = new Font("Segoe UI Semibold", 13.0f, FontStyle.Bold),
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(headerTextLeft, 13)
        };

        _lblSub = new Label
        {
            Text = _isUninstallMode
                ? $"Version {_version}  •  Preserves data\\, saves\\, and logs\\ unless purged below"
                : $"Version {_version}  •  {_subtitle}",
            Font = new Font("Segoe UI", 8.75f, FontStyle.Regular),
            ForeColor = TextMuted,
            AutoSize = true,
            Location = new Point(headerTextLeft, 43)
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
            ForeColor = TextPrimary,
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
            ForeColor = TextPrimary,
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
            ForeColor = TextPrimary,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _btnBrowse.FlatAppearance.BorderColor = BorderSubtle;
        _btnBrowse.Click += OnBrowseClicked;
    }
}
