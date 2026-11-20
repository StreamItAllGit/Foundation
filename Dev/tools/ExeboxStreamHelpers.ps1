#Requires -Version 5.1
# @module: Foundation.Tools.ExeboxStreamHelpers
# @purpose: Shared Desktop preflight, Test Viewer UDP 5004 launch/check, and exebox-stream start/stop helpers for Stage 2 E2E suites.
# @depends-on: exebox-stream/scripts/start_exebox_vlc_stream.ps1
# @exports: Get-ExeboxDesktopName, Test-ExeboxVlcViewerActive, Initialize-ExeboxTestViewerPreflight, Start-ExeboxLiveVlcStream, Stop-ExeboxLiveVlcStream

Set-StrictMode -Version Latest

if (-not ("FoundationExeboxDesktop" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class FoundationExeboxDesktop
{
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern IntPtr GetThreadDesktop(uint dwThreadId);
    [DllImport("user32.dll")] public static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetUserObjectInformationW(IntPtr hObj, int nIndex, StringBuilder pvInfo, uint nLength, out uint lpnLengthNeeded);

    public static string ReadDesktopPath()
    {
        var sbWs = new StringBuilder(256);
        var sbDk = new StringBuilder(256);
        uint needed = 0;
        string ws = GetUserObjectInformationW(GetProcessWindowStation(), 2, sbWs, 512, out needed) ? sbWs.ToString() : "UnknownWinSta";
        string dk = GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()), 2, sbDk, 512, out needed) ? sbDk.ToString() : "UnknownDesktop";
        return ws + "\\" + dk;
    }
}
"@
}

function Get-ExeboxDesktopName {
    return [FoundationExeboxDesktop]::ReadDesktopPath()
}

function Test-ExeboxVlcViewerActive {
    param([int]$UdpPort = 5004)
    $udp = Get-NetUDPEndpoint -LocalPort $UdpPort -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $udp) { return $true }
    $vlc = Get-Process -Name "vlc" -ErrorAction SilentlyContinue | Select-Object -First 1
    return ($null -ne $vlc)
}

function Initialize-ExeboxTestViewerPreflight {
    param([int]$UdpPort = 5004)
    $desktopName = Get-ExeboxDesktopName
    $isExebox = ($desktopName -like "*\exebox-*")
    $viewerActive = Test-ExeboxVlcViewerActive -UdpPort $UdpPort
    Write-Host ("[exebox-stream] Preflight: ThreadDesktop='{0}' | ViewerActive(UDP:{1})={2}" -f $desktopName, $UdpPort, $viewerActive)
    if (-not $viewerActive) {
        $lnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Test Viewer.lnk"
        if (-not $isExebox -and (Test-Path -LiteralPath $lnk)) {
            Start-Process -FilePath $lnk | Out-Null
            Start-Sleep -Milliseconds 500
            $viewerActive = Test-ExeboxVlcViewerActive -UdpPort $UdpPort
        } elseif ($isExebox) {
            Write-Host "[exebox-stream] Running inside isolated exebox desktop; NOT launching Test Viewer.lnk inside exebox. Click 'Test Viewer' in Start Menu (udp://@127.0.0.1:$UdpPort) to watch live."
        }
    }
    return [ordered]@{ desktopName = $desktopName; isExebox = $isExebox; viewerActive = $viewerActive }
}

function Start-ExeboxLiveVlcStream {
    param(
        [int]$AutomationPort = 0,
        [string]$AutomationToken = "",
        [int]$TargetPid = 0,
        [string]$WindowTitle = "",
        [string]$SnapshotDir = "",
        [int]$UdpPort = 5004,
        [int]$Fps = 20,
        [string]$LogPath = ""
    )
    $streamScript = Join-Path $env:USERPROFILE ".gemini\config\skills\exebox-stream\scripts\start_exebox_vlc_stream.ps1"
    if (-not (Test-Path -LiteralPath $streamScript)) { return $null }
    if (-not (Get-Command "ffmpeg" -ErrorAction SilentlyContinue)) { return $null }

    $null = Initialize-ExeboxTestViewerPreflight -UdpPort $UdpPort
    $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $streamScript, "-UdpHost", "127.0.0.1", "-UdpPort", "$UdpPort", "-Fps", "$Fps")
    if ($AutomationPort -gt 0) { $argList += @("-AutomationPort", "$AutomationPort") }
    if (-not [string]::IsNullOrWhiteSpace($AutomationToken)) { $argList += @("-AutomationToken", $AutomationToken) }
    if ($TargetPid -gt 0) { $argList += @("-TargetPid", "$TargetPid") }
    if (-not [string]::IsNullOrWhiteSpace($WindowTitle)) { $argList += @("-WindowTitle", $WindowTitle) }
    if (-not [string]::IsNullOrWhiteSpace($SnapshotDir)) { $argList += @("-SnapshotDir", $SnapshotDir) }
    if (-not [string]::IsNullOrWhiteSpace($LogPath)) { $argList += @("-LogPath", $LogPath) }

    $quoted = foreach ($a in $argList) {
        if ($null -eq $a -or $a -eq "") { '""' }
        elseif ($a -match '[\s"]') { '"' + ($a -replace '(\\*)"', '$1$1\"') + '"' }
        else { $a }
    }
    $psi = [System.Diagnostics.ProcessStartInfo]::new("powershell.exe")
    $psi.Arguments = $quoted -join " "
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    Write-Host ("[exebox-stream] Streaming PID={0} AutomationPort={1} -> udp://@127.0.0.1:{2} ({3} FPS)" -f $TargetPid, $AutomationPort, $UdpPort, $Fps)
    return [System.Diagnostics.Process]::Start($psi)
}

function Stop-ExeboxLiveVlcStream {
    param($StreamProc)
    if ($null -eq $StreamProc) { return }
    try {
        if (-not $StreamProc.HasExited) {
            & cmd.exe /c ("taskkill /T /F /PID " + $StreamProc.Id + " >nul 2>&1")
            $null = $StreamProc.WaitForExit(2000)
        }
    } catch { }
    try { $StreamProc.Dispose() } catch { }
}
