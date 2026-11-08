// @module: Foundation.Installer.UI.Wizard.ExecutionAndLogs
// @purpose: Upgrade badge detection, interactive FolderBrowserDialog browse handler, asynchronous install/upgrade/uninstall execution, progress logging, and post-setup launch.
// @depends-on: System, System.Diagnostics, System.IO, System.IO.Compression, System.Threading, System.Threading.Tasks, System.Windows.Forms
// @exports: InstallerWizardForm (partial: UpdateUpgradeDetectionBadge, OnBrowseClicked, OnPrimaryActionClickedAsync, AppendLogLine)

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Foundation.Installer;

public sealed partial class InstallerWizardForm
{
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
            MessageBox.Show(this, "Please specify a valid target installation folder.", $"{_displayName} Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _txtInstallDir.Enabled = false;
        _btnBrowse.Enabled = false;
        _chkShortcuts.Enabled = false;
        _chkAutoStart.Enabled = false;
        _chkPurgeData.Enabled = false;
        _btnPrimary.Enabled = false;

        _progressBar.Style = ProgressBarStyle.Marquee;
        _cts = new CancellationTokenSource();

        var progress = new Progress<string>(msg =>
        {
            if (IsDisposed) return;
            _lblStatus.Text = msg;
            AppendLogLine(msg);
        });

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
                    bool shortcuts = _chkShortcuts.Checked;
                    bool autoStart = _chkAutoStart.Checked;
                    result = await Task.Run(() => _fullEngine.InstallAsync(
                        explicitInstallDir: targetDir,
                        createShortcuts: shortcuts,
                        registerAutoStart: autoStart,
                        externalPayloadPath: _externalPayloadPath,
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
                    coreRes = await _customInstallAction(_cts.Token, (pct, msg) =>
                    {
                        _progressBar.Style = ProgressBarStyle.Blocks;
                        _progressBar.Value = Math.Clamp(pct, 0, 100);
                        _lblStatus.Text = msg;
                        AppendLogLine(msg);
                    });
                }
                else
                {
                    using var emptyZip = new MemoryStream();
                    using (new ZipArchive(emptyZip, ZipArchiveMode.Create, leaveOpen: true)) { }
                    emptyZip.Position = 0;
                    coreRes = await _coreEngine!.InstallOrUpgradeFromZipAsync(emptyZip, targetDir, cancellationToken: _cts.Token);
                }
                result = new InstallerOperationResult(coreRes.Success, coreRes.InstallDirectory, coreRes.PayloadSha256, coreRes.FileCount, coreRes.Message);
            }

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
                MessageBox.Show(this, result.Message, $"{_displayName} Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ReEnableInputsAfterFailure();
            }
        }
        catch (Exception ex)
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
            _progressBar.Value = 0;
            ExitCode = 1;
            _lblStatus.Text = "Setup failed: " + ex.Message;
            _lblStatus.ForeColor = AccentDanger;
            AppendLogLine("EXCEPTION: " + ex.Message);
            SetDetailsExpanded(true);
            MessageBox.Show(this, ex.Message, $"{_displayName} Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ReEnableInputsAfterFailure();
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
        _chkShortcuts.Enabled = true;
        _chkAutoStart.Enabled = true;
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
        catch { }
    }
}
