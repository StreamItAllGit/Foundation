// @module: Foundation.Installer.UI.Wizard.ExecutionAndLogs
// @purpose: Upgrade badge detection, Win32 IFileOpenDialog browse handler, asynchronous install/upgrade/uninstall execution, progress logging, and post-setup launch.
// @depends-on: System, System.Diagnostics, System.IO, System.IO.Compression, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: InstallerWizardForm (partial: UpdateUpgradeDetectionBadge, OnBrowseClicked, OnPrimaryActionClickedAsync, AppendLogLine)

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;

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
        _windowText = _isUninstallMode
            ? $"{_displayName} Uninstall (v{_version})"
            : $"{_displayName} Setup (v{_version})";
        _clientSize = new WizardSize(DefaultClientWidth, CollapsedClientHeight);

        InitializeHeaderAndDestinationControls(defaultDir, initialSecondaryDir);
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
        if (_isUninstallMode)
        {
            _lblModeBadge.Text = "UNINSTALL MODE";
            _lblModeBadge.ForeColor = AccentDanger;
            UpdateLayoutBounds();
            return;
        }

        string candidate = _txtInstallDir.Text.Trim();
        InstallDirectoryOwnership? ownership = _fullEngine?.CheckDirectoryOwnership(candidate).Status;
        if (ownership == InstallDirectoryOwnership.OwnedByForeignProduct ||
            (_coreEngine != null && InstallerEngine.CheckForeignOwnershipForTheme(candidate, _coreEngine.Theme.AppId, _coreEngine.Theme.DisplayName, out _)))
        {
            _lblModeBadge.Text = "FOREIGN APP DETECTED";
            _lblModeBadge.ForeColor = AccentDanger;
            if (!_completedSuccessfully)
            {
                _btnPrimary.Text = _buttonOverrides?.InstallText ?? "Install Now";
                _btnPrimary.BackColor = AccentDanger;
            }
            UpdateLayoutBounds();
            return;
        }

        bool existingInstall = _fullEngine != null
            ? _fullEngine.HasExistingInstallationArtifacts(candidate)
            : (_coreEngine != null && _coreEngine.HasExistingInstallation(candidate));

        if (existingInstall)
        {
            _lblModeBadge.Text = "UPGRADE DETECTED";
            _lblModeBadge.ForeColor = AccentEmerald;
            if (!_completedSuccessfully)
            {
                _btnPrimary.Text = _buttonOverrides?.UpgradeText ?? "Upgrade Now";
                _btnPrimary.BackColor = AccentEmerald;
            }
        }
        else
        {
            _lblModeBadge.Text = "FRESH INSTALL";
            _lblModeBadge.ForeColor = _accentColor;
            if (!_completedSuccessfully)
            {
                _btnPrimary.Text = _buttonOverrides?.InstallText ?? "Install Now";
                _btnPrimary.BackColor = _accentColor;
            }
        }

        UpdateLayoutBounds();
    }

    private void OnBrowseClicked(object? sender, EventArgs e)
    {
        SandboxResolution? sandbox = _ctx?.EffectiveSandbox ?? _coreEngine?.EffectiveSandbox;
        string initial = Directory.Exists(_txtInstallDir.Text)
            ? _txtInstallDir.Text
            : KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, sandbox);
        string? picked = Win32FolderPicker.PickFolder(_hwnd, $"Select {_displayName} installation directory", initial);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            _txtInstallDir.Text = picked;
        }
    }

    private void OnSecondaryBrowseClicked(object? sender, EventArgs e)
    {
        SandboxResolution? sandbox = _ctx?.EffectiveSandbox ?? _coreEngine?.EffectiveSandbox;
        string initial = Directory.Exists(_txtSecondaryDir.Text)
            ? _txtSecondaryDir.Text
            : KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, sandbox);
        string? picked = Win32FolderPicker.PickFolder(_hwnd, $"Select {_secondaryPickerOptions?.Label ?? "companion directory"}", initial);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            _txtSecondaryDir.Text = picked;
        }
    }

    private async Task OnPrimaryActionClickedAsync()
    {
        if (_completedSuccessfully)
        {
            bool suppressLaunch = _ctx?.ShouldSuppressHostMutations ?? (_coreEngine?.ShouldSuppressHostMutations ?? false);
            if (!_isUninstallMode && _chkLaunchAfter.Checked && !suppressLaunch)
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
            if (!ShouldSuppressModalDialogs && OperatingSystem.IsWindows())
            {
                _ = Win32Native.MessageBoxW(_hwnd, "Please specify a valid target installation folder.", $"{_displayName} Setup", Win32Native.MB_OK | Win32Native.MB_ICONWARNING);
            }
            return;
        }

        _txtInstallDir.Enabled = false;
        _btnBrowse.Enabled = false;
        _txtSecondaryDir.Enabled = false;
        _btnSecondaryBrowse.Enabled = false;
        _rbProfileLightweight.Enabled = false;
        _rbProfileFull.Enabled = false;
        _rbProfileCustom.Enabled = false;
        _chkShortcuts.Enabled = false;
        _chkAutoStart.Enabled = false;
        _chkMigrateLegacy.Enabled = false;
        _chkPurgeData.Enabled = false;
        _btnPrimary.Enabled = false;

        SetProgressMarquee(true);
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
                        MigrateLegacy: MigrateLegacyChecked)
                    {
                        OfflineBundlePath = _offlineBundlePath
                    };
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
                        SetProgressMarquee(false);
                        SetProgressValue(Math.Clamp(pct, 0, 100));
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
                SetProgressMarquee(false);
                if (result.Success)
                {
                    SetProgressValue(100);
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
                    SetProgressValue(0);
                    ExitCode = 1;
                    _lblStatus.Text = "Error: " + result.Message;
                    _lblStatus.ForeColor = AccentDanger;
                    AppendLogLine("ERROR: " + result.Message);
                    SetDetailsExpanded(true);
                    if (!ShouldSuppressModalDialogs && OperatingSystem.IsWindows())
                        _ = Win32Native.MessageBoxW(_hwnd, result.Message, $"{_displayName} Setup Error", Win32Native.MB_OK | Win32Native.MB_ICONERROR);
                    ReEnableInputsAfterFailure();
                }
            });
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
            {
                SetProgressMarquee(false);
                SetProgressValue(0);
                ExitCode = SandboxEnvironmentSeam.IsContainmentViolation(ex)
                    ? SandboxEnvironmentSeam.ContainmentExitCode
                    : 1;
                _lblStatus.Text = "Setup failed: " + ex.Message;
                _lblStatus.ForeColor = AccentDanger;
                AppendLogLine("EXCEPTION: " + ex.Message);
                SetDetailsExpanded(true);
                if (!ShouldSuppressModalDialogs && OperatingSystem.IsWindows())
                    _ = Win32Native.MessageBoxW(_hwnd, ex.Message, $"{_displayName} Setup Error", Win32Native.MB_OK | Win32Native.MB_ICONERROR);
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
        _rbProfileCustom.Enabled = !_isUninstallMode;
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
        RunOnUiThread(() =>
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss");
            _txtLog.AppendText($"[{stamp}] {line}{Environment.NewLine}");
        });
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

            if (_ctx?.IsSandboxActive == true)
                exePath = SandboxEnvironmentSeam.AssertSandboxPathIfActive(exePath, _ctx.Sandbox);
            else if (_coreEngine?.IsSandboxActive == true)
                exePath = SandboxEnvironmentSeam.AssertSandboxPathIfActive(exePath, _coreEngine.Sandbox);

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = installDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _ = ex;
        }
    }
}
