// @module: Foundation.Installer.BackupAndVerify
// @purpose: Fast <1.5s NoCompression pre-upgrade state backup, running daemon shutdown, and fail-closed path containment guards.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.IO.Compression, System.Linq, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: InstallerEngineCore (partial: CreatePreUpgradeBackupAsync, StopRunningInstancesInDirectory, EnsureWithinDirectory)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;

namespace Foundation.Installer;

public sealed partial class InstallerEngineCore
{
    private static readonly HashSet<string> PreCompressedOrDatabaseExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".zip", ".rar", ".gz", ".chd", ".rvz", ".iso", ".db", ".db-wal", ".db-shm", ".tsv", ".manifest"
    };

    public static async Task<string?> CreatePreUpgradeBackupAsync(
        string installDir,
        string version,
        IReadOnlyList<string>? topFilesToBackup = null,
        IReadOnlyList<string>? subDirsToBackup = null,
        IProgress<string>? progress = null,
        Action<string, string>? logCallback = null,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        if (dryRun || string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
            return null;

        string backupsDir = EnsureWithinDirectory(installDir, Path.Combine(installDir, "backups"));
        Directory.CreateDirectory(backupsDir);

        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string safeVer = (version ?? "0.0.0").Replace('/', '-').Replace('\\', '-').Replace(':', '-');
        string backupZipPath = Path.Combine(backupsDir, $"upgrade_pre_v{safeVer}_{stamp}.zip");
        string tempZipPath = backupZipPath + ".tmp_" + Guid.NewGuid().ToString("N")[..6];

        string[] defaultTopFiles =
        [
            "settings.json", "shadowlibrary.db", "shadowlibrary.db-wal",
            "ShadowLibrary.manifest", "ShadowLibrary.manifest.tsv", ".sl_cookie_secret.key", "install-manifest.json"
        ];
        string[] defaultSubDirs = ["registry", "saves_vault", "saves", "dats_ra", "config_history"];

        var topFiles = topFilesToBackup ?? defaultTopFiles;
        var subDirs = subDirsToBackup ?? defaultSubDirs;

        try
        {
            ct.ThrowIfCancellationRequested();
            var eligibleFiles = new List<(string FullPath, string RelPath, long Length)>();
            var seenRel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool hasRootManifestTsv = File.Exists(Path.Combine(installDir, "ShadowLibrary.manifest.tsv"));
            foreach (string fileName in topFiles)
            {
                if (hasRootManifestTsv && string.Equals(fileName, "ShadowLibrary.manifest", StringComparison.OrdinalIgnoreCase))
                    continue;
                string src = Path.Combine(installDir, fileName);
                if (File.Exists(src) && seenRel.Add(fileName))
                    eligibleFiles.Add((src, fileName, new FileInfo(src).Length));
            }

            foreach (string subDirName in subDirs)
            {
                string subDirPath = Path.Combine(installDir, subDirName);
                if (!Directory.Exists(subDirPath)) continue;

                foreach (string file in Directory.GetFiles(subDirPath, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(installDir, file).Replace('\\', '/');
                    if (rel.EndsWith("-shm", StringComparison.OrdinalIgnoreCase) || !seenRel.Add(rel))
                        continue;
                    eligibleFiles.Add((file, rel, new FileInfo(file).Length));
                }
            }

            string dataDirPath = Path.Combine(installDir, "data");
            if (Directory.Exists(dataDirPath))
            {
                bool hasDataManifestTsv = File.Exists(Path.Combine(dataDirPath, "ShadowLibrary.manifest.tsv"));
                foreach (string file in Directory.GetFiles(dataDirPath, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(installDir, file).Replace('\\', '/');
                    if (rel.StartsWith("data/webview2_cache/", StringComparison.OrdinalIgnoreCase) ||
                        rel.StartsWith("data/_Quarantine/", StringComparison.OrdinalIgnoreCase) ||
                        rel.StartsWith("data/drops/", StringComparison.OrdinalIgnoreCase) ||
                        rel.StartsWith("data/backups/", StringComparison.OrdinalIgnoreCase) ||
                        rel.EndsWith("-shm", StringComparison.OrdinalIgnoreCase) ||
                        (hasDataManifestTsv && string.Equals(rel, "data/ShadowLibrary.manifest", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (seenRel.Add(rel))
                        eligibleFiles.Add((file, rel, new FileInfo(file).Length));
                }
            }

            int totalFiles = eligibleFiles.Count;
            if (totalFiles == 0)
                return null;

            long totalBytes = eligibleFiles.Sum(f => f.Length);
            string startMsg = $"Creating pre-upgrade state backup ({totalFiles} files, {totalBytes / (1024.0 * 1024.0):F1} MB)...";
            logCallback?.Invoke("INFO", startMsg);
            progress?.Report(startMsg);

            int filesDone = 0;
            long bytesDone = 0;
            var heartbeatSw = Stopwatch.StartNew();

            await using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var (fullPath, rel, len) in eligibleFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    await AddFileSharedReadToZipAsync(zip, fullPath, rel, ct).ConfigureAwait(false);
                    filesDone++;
                    bytesDone += len;

                    if (filesDone == 1 || filesDone == totalFiles || filesDone % 25 == 0 || heartbeatSw.ElapsedMilliseconds >= 2000)
                    {
                        heartbeatSw.Restart();
                        progress?.Report($"Backing up state ({filesDone}/{totalFiles} files, {bytesDone / (1024.0 * 1024.0):F1} / {totalBytes / (1024.0 * 1024.0):F1} MB): {rel}");
                    }
                }
            }

            FileLockRetryHelper.MoveWithOverwrite(tempZipPath, backupZipPath, onRetryLog: msg => logCallback?.Invoke("WARN", msg), ct: ct);

            var oldBackups = new DirectoryInfo(backupsDir)
                .GetFiles("upgrade_pre_*.zip")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(5)
                .ToList();
            foreach (var old in oldBackups)
            {
                try { FileLockRetryHelper.DeleteIfExists(old.FullName, onRetryLog: msg => logCallback?.Invoke("WARN", msg), ct: ct); } catch { }
            }

            logCallback?.Invoke("INFO", $"Created pre-upgrade backup '{backupZipPath}' ({totalFiles} files, {totalBytes / (1024.0 * 1024.0):F1} MB).");
            return backupZipPath;
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(tempZipPath)) { try { File.Delete(tempZipPath); } catch { } }
            logCallback?.Invoke("WARN", "Pre-upgrade state backup canceled.");
            throw;
        }
        catch (Exception ex)
        {
            if (File.Exists(tempZipPath)) { try { File.Delete(tempZipPath); } catch { } }
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);
            logCallback?.Invoke("WARN", $"Pre-upgrade backup skipped: {ex.Message}");
            return null;
        }
    }

    private static async Task AddFileSharedReadToZipAsync(ZipArchive zip, string sourceFilePath, string entryName, CancellationToken ct)
    {
        var fi = new FileInfo(sourceFilePath);
        CompressionLevel level = PreCompressedOrDatabaseExtensions.Contains(Path.GetExtension(sourceFilePath)) ||
                                 (fi.Exists && fi.Length >= 8L * 1024 * 1024)
            ? CompressionLevel.NoCompression
            : CompressionLevel.Fastest;
        var entry = zip.CreateEntry(entryName, level);
        entry.LastWriteTime = File.GetLastWriteTimeUtc(sourceFilePath);
        await using var fs = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var entryStream = entry.Open();
        await fs.CopyToAsync(entryStream, 81920, ct).ConfigureAwait(false);
    }

    public static bool StopRunningInstancesInDirectory(string installDir, IReadOnlyList<string> processNames, bool dryRun = false)
    {
        if (dryRun || !Directory.Exists(installDir) || processNames == null)
            return false;

        bool stoppedAny = false;
        foreach (string procName in processNames)
        {
            foreach (var proc in Process.GetProcessesByName(procName))
            {
                try
                {
                    string? procPath = proc.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(procPath) && IsWithinDirectory(installDir, procPath))
                    {
                        stoppedAny = true;
                        try
                        {
                            if (proc.MainWindowHandle != IntPtr.Zero && proc.CloseMainWindow())
                                proc.WaitForExit(1500);
                        }
                        catch { }

                        if (!proc.HasExited)
                        {
                            proc.Kill(entireProcessTree: true);
                            proc.WaitForExit(5000);
                        }
                    }
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }

        if (stoppedAny) Thread.Sleep(300);
        return stoppedAny;
    }

    public static string EnsureWithinDirectory(string boundaryDir, string candidatePath)
    {
        string normalizedBoundary = Path.GetFullPath(boundaryDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        string fullCandidate = Path.GetFullPath(candidatePath);
        string candidateWithSlash = fullCandidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!candidateWithSlash.StartsWith(normalizedBoundary, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Fail-closed path containment violation: target path '{fullCandidate}' escapes boundary '{normalizedBoundary}'.");
        }

        return fullCandidate;
    }

    public static bool IsWithinDirectory(string boundaryDir, string candidatePath)
    {
        try
        {
            EnsureWithinDirectory(boundaryDir, candidatePath);
            return true;
        }
        catch { return false; }
    }

    public string? DetectLegacyFlatInstall(string? legacyBaseDir = null)
    {
        string searchRoot;
        if (!string.IsNullOrWhiteSpace(legacyBaseDir))
        {
            searchRoot = Path.GetFullPath(legacyBaseDir);
            if (ShouldSuppressLiveHostMutations && FileLockRetryHelper.IsCanonicalProductionOrSystemPath(searchRoot, EffectiveSandbox))
            {
                SandboxEnvironmentSeam.ThrowContainmentViolation(
                    $"Fail-closed isolation violation: refusing to scan canonical production or system path '{searchRoot}'.");
            }
            if (IsSandboxActive)
            {
                searchRoot = SandboxEnvironmentSeam.AssertSandboxPathIfActive(searchRoot, Sandbox);
            }
        }
        else if (IsSandboxActive)
        {
            string storageRoot = !string.IsNullOrWhiteSpace(Sandbox.StorageDriveRoot)
                ? Sandbox.StorageDriveRoot
                : Path.Combine(Sandbox.SandboxRoot!, "D");
            searchRoot = Path.Combine(storageRoot, "Programs");
        }
        else if (IsStage1Isolated)
        {
            searchRoot = Path.Combine(RuntimeEnvironment.DataDirectory, "legacy-programs");
        }
        else
        {
            searchRoot = Path.Combine("D:" + Path.DirectorySeparatorChar, "Programs");
        }

        if (!Directory.Exists(searchRoot))
        {
            return null;
        }

        foreach (string folderName in new[] { Theme.DisplayName, Theme.AppId })
        {
            if (string.IsNullOrWhiteSpace(folderName)) continue;
            string candidate = Path.Combine(searchRoot, folderName.Trim());
            if (HasExistingInstallation(candidate) || File.Exists(Path.Combine(candidate, "settings.json")))
            {
                return candidate;
            }
        }

        return null;
    }

    public async Task<FoundationInstallResult> MigrateLegacyInstallAsync(
        string legacySourceDir,
        string? targetInstallDir = null,
        bool optInConfirmed = false,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (!optInConfirmed)
        {
            throw new InvalidOperationException("Legacy install migration requires explicit opt-in (--migrate-legacy or wizard checkbox).");
        }

        if (string.IsNullOrWhiteSpace(legacySourceDir))
        {
            throw new ArgumentException("Legacy source directory is required.", nameof(legacySourceDir));
        }

        string fullSource = Path.GetFullPath(legacySourceDir);
        string fullTarget = ResolveTargetInstallDir(targetInstallDir);
        if (ShouldSuppressLiveHostMutations &&
            (FileLockRetryHelper.IsCanonicalProductionOrSystemPath(fullSource, EffectiveSandbox) ||
             FileLockRetryHelper.IsCanonicalProductionOrSystemPath(fullTarget, EffectiveSandbox)))
        {
            SandboxEnvironmentSeam.ThrowContainmentViolation(
                "Fail-closed isolation violation: refusing to migrate canonical production or system directories.");
        }
        if (IsSandboxActive)
        {
            fullSource = SandboxEnvironmentSeam.AssertSandboxPathIfActive(fullSource, Sandbox);
            fullTarget = SandboxEnvironmentSeam.AssertSandboxPathIfActive(fullTarget, Sandbox);
        }

        if (!Directory.Exists(fullSource))
        {
            return new FoundationInstallResult(false, fullTarget, string.Empty, 0, $"Legacy directory not found: '{fullSource}'.", false);
        }

        if (string.Equals(fullSource.TrimEnd(Path.DirectorySeparatorChar), fullTarget.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return new FoundationInstallResult(true, fullTarget, string.Empty, 0, "Legacy source and target directory are identical.", false);
        }

        if (RuntimeEnvironment.DryRun || dryRun)
        {
            return new FoundationInstallResult(true, fullTarget, string.Empty, 0, $"[DRY-RUN] Would migrate legacy install from '{fullSource}' to '{fullTarget}'.", false);
        }

        Directory.CreateDirectory(fullTarget);
        int migratedCount = 0;

        foreach (string entryName in ProtectedTopDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string srcSub = Path.Combine(fullSource, entryName);
            string dstSub = Path.Combine(fullTarget, entryName);
            if (Directory.Exists(srcSub))
            {
                migratedCount += await CopyDirectoryPreservingAsync(srcSub, dstSub, cancellationToken).ConfigureAwait(false);
            }
        }

        string srcSettings = Path.Combine(fullSource, "settings.json");
        string dstSettings = Path.Combine(fullTarget, "settings.json");
        if (File.Exists(srcSettings) && !File.Exists(dstSettings))
        {
            FileLockRetryHelper.CopyWithOverwrite(srcSettings, dstSettings, ct: cancellationToken);
            migratedCount++;
        }

        try { Directory.Delete(fullSource, recursive: true); } catch { /* non-fatal if locked */ }

        return new FoundationInstallResult(true, fullTarget, string.Empty, migratedCount, $"Migrated {migratedCount} legacy items from '{fullSource}' to '{fullTarget}'.", false);
    }

    private static async Task<int> CopyDirectoryPreservingAsync(string sourceDir, string targetDir, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);
        int count = 0;
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string rel = Path.GetRelativePath(sourceDir, file);
            string dest = Path.Combine(targetDir, rel);
            string? parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            if (!File.Exists(dest))
            {
                await using var inFs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                await using var outFs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await inFs.CopyToAsync(outFs, ct).ConfigureAwait(false);
                count++;
            }
        }
        return count;
    }
}
