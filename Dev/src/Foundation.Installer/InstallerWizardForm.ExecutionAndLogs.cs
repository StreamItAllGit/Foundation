// @module: Foundation.Installer.UI.Wizard.ExecutionAndLogs
// @purpose: Upgrade badge detection, interactive FolderBrowserDialog browse handler, asynchronous install/upgrade/uninstall execution, progress logging, and post-setup launch.
// @depends-on: System, System.Diagnostics, System.IO, System.IO.Compression, System.Threading, System.Threading.Tasks, System.Windows.Forms
// @exports: InstallerWizardForm (partial: UpdateUpgradeDetectionBadge, OnBrowseClicked, OnPrimaryActionClickedAsync, AppendLogLine)

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Foundation.Installer;

public sealed partial class InstallerWizardForm
{
    private void InitializeWizardSurface(
        string defaultDir,
        bool initialCreateShortcuts,
        bool initialAutoStart,
        bool initialLaunchAfter,
        bool initialPurgeData,
        string initialProfileId,
        string? initialSecondaryDir,
        bool initialMigrateLegacy)
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
        catch (Exception ex)
        {
            _ = ex; // Icon extraction failure is non-fatal in headless/test hosts.
        }

        InitializeHeaderAndDestinationControls(defaultDir, initialSecondaryDir, extractedIcon);
        InitializeOptionsAndFooterControls(
            initialCreateShortcuts,
            initialAutoStart,
            initialLaunchAfter,
            initialPurgeData,
            initialProfileId,
            initialMigrateLegacy);

        _controlsInitialized = true;
        UpdateUpgradeDetectionBadge();
        UpdateLayoutBounds();

        AppendLogLine($"{_displayName} Setup v{_version} initialized.");
        AppendLogLine($"Target directory: {_txtInstallDir.Text}");
        if (_hasSecondaryPicker)
        {
            AppendLogLine($"Secondary directory ({_secondaryPickerOptions?.FlagName}): {_txtSecondaryDir.Text}");
        }
    }

    private void UpdateUpgradeDetectionBadge()
    {
        if (_autoRunVisibleTest || _showcaseAutoCloseMs > 0)
        {
            _lblModeBadge.Text = "VISIBLE TEST";
            _lblModeBadge.ForeColor = _accentColor;
            UpdateLayoutBounds();
            return;
        }

        if (_isUninstallMode)
        {
            _lblModeBadge.Text = "UNINSTALL MODE";
            _lblModeBadge.ForeColor = AccentDanger;
            UpdateLayoutBounds();
            return;
        }

        string candidate = _txtInstallDir.Text.Trim();
        bool existingInstall = _fullEngine != null
            ? _fullEngine.HasExistingInstallationArtifacts(candidate)
            : (_coreEngine != null && _coreEngine.HasExistingInstallation(candidate));

        if (existingInstall)
        {
            _lblModeBadge.Text = "UPGRADE DETECTED";
            _lblModeBadge.ForeColor = AccentEmerald;
            if (!_completedSuccessfully)
            {
                _btnPrimary.Text = "Upgrade Now";
                _btnPrimary.BackColor = AccentEmerald;
            }
        }
        else
        {
            _lblModeBadge.Text = "FRESH INSTALL";
            _lblModeBadge.ForeColor = _accentColor;
            if (!_completedSuccessfully)
            {
                _btnPrimary.Text = "Install Now";
                _btnPrimary.BackColor = _accentColor;
            }
        }

        UpdateLayoutBounds();
    }

    private void OnBrowseClicked(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = $"Select {_displayName} installation directory",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_txtInstallDir.Text)
                ? _txtInstallDir.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            _txtInstallDir.Text = dialog.SelectedPath;
        }
    }

    private void OnSecondaryBrowseClicked(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = $"Select {_secondaryPickerOptions?.Label ?? "companion directory"}",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_txtSecondaryDir.Text)
                ? _txtSecondaryDir.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            _txtSecondaryDir.Text = dialog.SelectedPath;
        }
    }

    private async Task OnPrimaryActionClickedAsync()
    {
        if (_completedSuccessfully)
        {
            bool isContained = _ctx?.IsContainedMode ?? (_coreEngine?.Sandbox.IsContainedSandbox ?? false);
            if (!_isUninstallMode && _chkLaunchAfter.Checked && !isContained)
            {
                TryLaunchInstalledApplication(_completedInstallDir);
            }
            ExitCode = 0;
            Close();
            return;
        }

        string targetDir = _txtInstallDir.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            if (!ShouldSuppressModalDialogs)
            {
                MessageBox.Show(this, "Please specify a valid target installation folder.", $"{_displayName} Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return;
        }

        _txtInstallDir.Enabled = false;
        _btnBrowse.Enabled = false;
        _txtSecondaryDir.Enabled = false;
        _btnSecondaryBrowse.Enabled = false;
        _rbProfileLightweight.Enabled = false;
        _rbProfileFull.Enabled = false;
        _chkShortcuts.Enabled = false;
        _chkAutoStart.Enabled = false;
        _chkMigrateLegacy.Enabled = false;
        _chkPurgeData.Enabled = false;
        _btnPrimary.Enabled = false;

        _progressBar.Style = ProgressBarStyle.Marquee;
        _cts = new CancellationTokenSource();

        var progress = new Progress<string>(msg => RunOnUiThread(() =>
        {
            _lblStatus.Text = msg;
            AppendLogLine(msg);
        }));

        try
        {
            InstallerOperationResult result;
            if (_fullEngine != null)
            {
                if (_isUninstallMode)
                {
                    bool purge = _chkPurgeData.Checked;
                    result = await Task.Run(() => _fullEngine.UninstallAsync(
                        explicitInstallDir: targetDir,
                        purgeData: purge,
                        ct: _cts.Token,
                        progress: progress));
                }
                else
                {
                    var installOpts = new InstallerInstallOptions(
                        ExplicitInstallDir: targetDir,
                        CreateShortcuts: _chkShortcuts.Checked,
                        RegisterAutoStart: _allowAutoStart && _chkAutoStart.Checked,
                        ExternalPayloadPath: _externalPayloadPath,
                        InstallProfile: SelectedProfileId,
                        SecondaryInstallDir: _hasSecondaryPicker && !string.IsNullOrWhiteSpace(SecondaryInstallDir) ? SecondaryInstallDir : null,
                        SecondaryFlagName: _secondaryPickerOptions?.FlagName,
                        MigrateLegacy: MigrateLegacyChecked);
                    result = await Task.Run(() => _fullEngine.InstallAsync(
                        installOpts,
                        ct: _cts.Token,
                        progress: progress));
                }
            }
            else
            {
                FoundationInstallResult coreRes;
                if (_isUninstallMode)
                {
                    coreRes = _coreEngine!.Uninstall(targetDir, _chkPurgeData.Checked);
                }
                else if (_customInstallAction != null)
                {
                    coreRes = await _customInstallAction(_cts.Token, (pct, msg) => RunOnUiThread(() =>
                    {
                        _progressBar.Style = ProgressBarStyle.Blocks;
                        _progressBar.Value = Math.Clamp(pct, 0, 100);
                        _lblStatus.Text = msg;
                        AppendLogLine(msg);
                    }));
                }
                else
                {
                    if (_coreEngine != null && MigrateLegacyChecked && !string.IsNullOrWhiteSpace(_legacyDetectedPath))
                    {
                        await _coreEngine.MigrateLegacyInstallAsync(_legacyDetectedPath, targetDir, optInConfirmed: true, cancellationToken: _cts.Token);
                    }
                    using var emptyZip = new MemoryStream();
                    using (new ZipArchive(emptyZip, ZipArchiveMode.Create, leaveOpen: true)) { }
                    emptyZip.Position = 0;
                    coreRes = await _coreEngine!.InstallOrUpgradeFromZipAsync(emptyZip, targetDir, cancellationToken: _cts.Token);
                }
                result = new InstallerOperationResult(coreRes.Success, coreRes.InstallDirectory, coreRes.PayloadSha256, coreRes.FileCount, coreRes.Message);
            }

            RunOnUiThread(() =>
            {
                _progressBar.Style = ProgressBarStyle.Blocks;
                if (result.Success)
                {
                    _progressBar.Value = 100;
                    _completedSuccessfully = true;
                    _completedInstallDir = result.InstallDirectory;
                    ExitCode = 0;

                    _lblStatus.Text = result.Message;
                    _lblStatus.ForeColor = AccentEmerald;
                    AppendLogLine("SUCCESS: " + result.Message);

                    _btnPrimary.Text = !_isUninstallMode && _chkLaunchAfter.Checked ? "Launch" : "Finish";
                    _btnPrimary.BackColor = AccentEmerald;
                    _btnPrimary.Enabled = true;
                    _btnCancel.Visible = false;
                    UpdateLayoutBounds();
                }
                else
                {
                    _progressBar.Value = 0;
                    ExitCode = 1;
                    _lblStatus.Text = "Error: " + result.Message;
                    _lblStatus.ForeColor = AccentDanger;
                    AppendLogLine("ERROR: " + result.Message);
                    SetDetailsExpanded(true);
                    if (!ShouldSuppressModalDialogs)
                        MessageBox.Show(this, result.Message, $"{_displayName} Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    ReEnableInputsAfterFailure();
                }
            });
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
            {
                _progressBar.Style = ProgressBarStyle.Blocks;
                _progressBar.Value = 0;
                ExitCode = 1;
                _lblStatus.Text = "Setup failed: " + ex.Message;
                _lblStatus.ForeColor = AccentDanger;
                AppendLogLine("EXCEPTION: " + ex.Message);
                SetDetailsExpanded(true);
                if (!ShouldSuppressModalDialogs)
                    MessageBox.Show(this, ex.Message, $"{_displayName} Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ReEnableInputsAfterFailure();
            });
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ReEnableInputsAfterFailure()
    {
        _txtInstallDir.Enabled = true;
        _btnBrowse.Enabled = true;
        _txtSecondaryDir.Enabled = _hasSecondaryPicker;
        _btnSecondaryBrowse.Enabled = _hasSecondaryPicker;
        _rbProfileLightweight.Enabled = !_isUninstallMode;
        _rbProfileFull.Enabled = !_isUninstallMode;
        _chkShortcuts.Enabled = !_isUninstallMode;
        _chkAutoStart.Enabled = _allowAutoStart && !_isUninstallMode;
        _chkMigrateLegacy.Enabled = _showLegacyMigration;
        _chkPurgeData.Enabled = true;
        _btnPrimary.Enabled = true;
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        if (_cts != null)
        {
            _cts.Cancel();
            return;
        }

        ExitCode = 1;
        Close();
    }

    public void AppendLogLine(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLogLine), line);
            return;
        }

        string stamp = DateTime.Now.ToString("HH:mm:ss");
        _txtLog.AppendText($"[{stamp}] {line}{Environment.NewLine}");
    }

    private void TryLaunchInstalledApplication(string installDir)
    {
        try
        {
            string exePath = _fullEngine != null
                ? _fullEngine.ResolveInstalledExecutablePath(installDir)
                : Path.Combine(installDir, _coreEngine?.Theme.ExecutableFileName ?? $"{_displayName}.exe");

            if (!File.Exists(exePath))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = installDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _ = ex; // Optional post-install launch failure does not negate completed install.
        }
    }
}
