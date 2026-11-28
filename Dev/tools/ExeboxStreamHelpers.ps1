#Requires -Version 5.1
# @module: Foundation.Tools.ExeboxStreamHelpers
# @purpose: Shared Desktop preflight, Always-On Web Test Viewer Hub (http://127.0.0.1:5005) check, and suite-wide Virtual Desktop Compositor helpers (+ backward-compatible legacy shims) for Stage 2 E2E suites.
# @depends-on: exebox-stream/bin/DesktopCapture/ExeboxDesktopCapture.exe
# @exports: Get-ExeboxDesktopName, Get-ExeboxHubHealth, Test-ExeboxViewerActive, Test-ExeboxVlcViewerActive, Initialize-ExeboxTestViewerPreflight, Start-ExeboxDesktopStream, Attach-ExeboxAutomationWindow, Detach-ExeboxAutomationWindow, Send-ExeboxStreamEvent, Stop-ExeboxDesktopStream, Start-ExeboxLiveVlcStream, Stop-ExeboxLiveVlcStream

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

function Get-ExeboxCaptureExePath {
    return (Join-Path $env:USERPROFILE ".gemini\config\skills\exebox-stream\bin\DesktopCapture\ExeboxDesktopCapture.exe")
}

function Get-ExeboxHubHealth {
    param([string]$HubUrl = "http://127.0.0.1:5005")
    try {
        $resp = Invoke-RestMethod -Uri ($HubUrl.TrimEnd('/') + "/api/v1/health") -Method Get -TimeoutSec 2 -ErrorAction Stop
        if ($null -ne $resp -and $resp.ok) { return $resp }
    } catch { }
    return $null
}

function Test-ExeboxViewerActive {
    param([string]$HubUrl = "http://127.0.0.1:5005")
    $health = Get-ExeboxHubHealth -HubUrl $HubUrl
    return ($null -ne $health)
}

function Test-ExeboxVlcViewerActive {
    param([int]$UdpPort = 5004, [string]$HubUrl = "http://127.0.0.1:5005")
    $null = $UdpPort
    return (Test-ExeboxViewerActive -HubUrl $HubUrl)
}

function Initialize-ExeboxTestViewerPreflight {
    param([int]$UdpPort = 5004, [string]$HubUrl = "http://127.0.0.1:5005")
    $null = $UdpPort
    $desktopName = Get-ExeboxDesktopName
    $isExebox = ($desktopName -like "*\exebox-*")
    $health = Get-ExeboxHubHealth -HubUrl $HubUrl
    $hubActive = ($null -ne $health)
    $viewerCount = if ($hubActive) { [int]$health.viewerCount } else { 0 }
    Write-Host ("[exebox-stream] Preflight: ThreadDesktop='{0}' | HubActive({1})={2} | Viewers={3}" -f $desktopName, $HubUrl, $hubActive, $viewerCount)

    if ((-not $hubActive -or $viewerCount -le 0)) {
        $lnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Test Viewer.lnk"
        if (-not $isExebox -and (Test-Path -LiteralPath $lnk)) {
            Start-Process -FilePath $lnk | Out-Null
            Start-Sleep -Milliseconds 700
            $health = Get-ExeboxHubHealth -HubUrl $HubUrl
            $hubActive = ($null -ne $health)
            $viewerCount = if ($hubActive) { [int]$health.viewerCount } else { 0 }
        } elseif ($isExebox -and $viewerCount -le 0) {
            Write-Host "[exebox-stream] Running inside isolated exebox desktop; NOT launching Test Viewer inside exebox. Click 'Test Viewer' in Start Menu ($HubUrl) to watch live."
        }
    }
    return [ordered]@{ desktopName = $desktopName; isExebox = $isExebox; viewerActive = $hubActive; hubActive = $hubActive; viewerCount = $viewerCount; hubUrl = $HubUrl }
}

function Format-ExeboxProcessArgs([string[]]$ArgList) {
    $quoted = foreach ($a in $ArgList) {
        if ($null -eq $a -or $a -eq "") { '""' }
        elseif ($a -match '[\s"]') { '"' + ($a -replace '(\\*)"', '$1$1\"') + '"' }
        else { $a }
    }
    return ($quoted -join " ")
}

function Start-ExeboxDesktopStream {
    param(
        [string]$Project = "Sandbox E2E",
        [string]$HubUrl = "http://127.0.0.1:5005",
        [int]$Fps = 20,
        [int]$StreamWidth = 1280,
        [int]$StreamHeight = 720,
        [int]$AutomationPort = 0,
        [string]$AutomationToken = "",
        [int]$TargetPid = 0,
        [string]$WindowTitle = "",
        [string]$SnapshotDir = "",
        [string]$LogPath = "",
        [switch]$StopOnTargetExit,
        [switch]$CompatFfmpegMarker
    )
    $capExe = Get-ExeboxCaptureExePath
    if (-not (Test-Path -LiteralPath $capExe)) { return $null }

    $null = Initialize-ExeboxTestViewerPreflight -HubUrl $HubUrl
    $sessionId = "s2-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
    $ctrlPortFile = Join-Path $env:TEMP ("exebox_ctrl_" + $sessionId + ".txt")
    $resolvedLog = if (-not [string]::IsNullOrWhiteSpace($LogPath)) { $LogPath } else { Join-Path $env:TEMP "exebox_stream_5005.log" }
    $clampedFps = [Math]::Min(30, [Math]::Max(5, $Fps))

    $argList = @(
        "--hub", $HubUrl.TrimEnd('/'),
        "--project", $Project,
        "--session-id", $sessionId,
        "--fps", "$clampedFps",
        "--width", "$StreamWidth",
        "--height", "$StreamHeight",
        "--control-port", "0",
        "--control-port-file", $ctrlPortFile,
        "--log-path", $resolvedLog
    )
    if ($AutomationPort -gt 0) { $argList += @("--automation-port", "$AutomationPort") }
    if (-not [string]::IsNullOrWhiteSpace($AutomationToken)) { $argList += @("--automation-token", $AutomationToken) }
    if ($TargetPid -gt 0) { $argList += @("--target-pid", "$TargetPid") }
    if (-not [string]::IsNullOrWhiteSpace($WindowTitle)) { $argList += @("--window-title", $WindowTitle) }
    if (-not [string]::IsNullOrWhiteSpace($SnapshotDir)) { $argList += @("--snapshot-dir", $SnapshotDir) }
    if ($StopOnTargetExit) { $argList += "--stop-on-target-exit" }
    if ($CompatFfmpegMarker) { $argList += "--compat-ffmpeg-marker" }

    $psi = [System.Diagnostics.ProcessStartInfo]::new($capExe)
    $psi.Arguments = Format-ExeboxProcessArgs $argList
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $proc = [System.Diagnostics.Process]::Start($psi)

    $ctrlPort = 0
    for ($i = 0; $i -lt 40; $i++) {
        if ($proc.HasExited) { break }
        if (Test-Path -LiteralPath $ctrlPortFile) {
            $raw = (Get-Content -LiteralPath $ctrlPortFile -Raw -ErrorAction SilentlyContinue)
            if ($raw -and [int]::TryParse($raw.Trim(), [ref]$ctrlPort) -and $ctrlPort -gt 0) { break }
        }
        Start-Sleep -Milliseconds 50
    }
    Write-Host ("[exebox-stream] Virtual Desktop Stream PID={0} ControlPort={1} -> {2} ({3} FPS)" -f $proc.Id, $ctrlPort, $HubUrl, $clampedFps)
    return [PSCustomObject]@{
        Process         = $proc
        ControlPort     = $ctrlPort
        ControlPortFile = $ctrlPortFile
        HubUrl          = $HubUrl.TrimEnd('/')
        SessionId       = $sessionId
        Project         = $Project
        LogPath         = $resolvedLog
    }
}

function Attach-ExeboxAutomationWindow {
    param(
        $Stream,
        [int]$AutomationPort,
        [string]$AutomationToken = "",
        [int]$TargetPid = 0,
        [string]$Label = "",
        [string]$SnapshotDir = ""
    )
    if ($null -eq $Stream -or $Stream.ControlPort -le 0 -or $AutomationPort -le 0) { return $false }
    $body = [ordered]@{ automationPort = $AutomationPort; automationToken = $AutomationToken; targetPid = $TargetPid; label = $Label }
    if (-not [string]::IsNullOrWhiteSpace($SnapshotDir)) { $body.snapshotDir = $SnapshotDir }
    try {
        $null = Invoke-RestMethod -Uri ("http://127.0.0.1:{0}/attach" -f $Stream.ControlPort) -Method Post -ContentType "application/json" -Body ($body | ConvertTo-Json -Compress) -TimeoutSec 2
        return $true
    } catch { return $false }
}

function Detach-ExeboxAutomationWindow {
    param($Stream, [int]$AutomationPort = 0, [int]$TargetPid = 0)
    if ($null -eq $Stream -or $Stream.ControlPort -le 0) { return $false }
    try {
        $body = @{ automationPort = $AutomationPort; targetPid = $TargetPid } | ConvertTo-Json -Compress
        $null = Invoke-RestMethod -Uri ("http://127.0.0.1:{0}/detach" -f $Stream.ControlPort) -Method Post -ContentType "application/json" -Body $body -TimeoutSec 2
        return $true
    } catch { return $false }
}

function Send-ExeboxStreamEvent {
    param(
        $Stream = $null,
        [string]$HubUrl = "http://127.0.0.1:5005",
        [string]$Project = "",
        [string]$Type = "step",
        [string]$StepId = "",
        [string]$Title = "",
        [string]$Status = "info",
        [string]$Message = "",
        [string]$InputKind = "",
        [double]$NormX = 0.5,
        [double]$NormY = 0.5,
        [string]$InputLabel = ""
    )
    $resolvedHub = if ($null -ne $Stream -and $Stream.HubUrl) { $Stream.HubUrl } else { $HubUrl.TrimEnd('/') }
    $resolvedProj = if (-not [string]::IsNullOrWhiteSpace($Project)) { $Project } elseif ($null -ne $Stream -and $Stream.Project) { $Stream.Project } else { "Sandbox E2E" }
    $resolvedSid = if ($null -ne $Stream -and $Stream.SessionId) { $Stream.SessionId } else { "" }
    $body = [ordered]@{
        type = $Type; sessionId = $resolvedSid; project = $resolvedProj
        stepId = $StepId; stepTitle = $Title; status = $Status; message = $Message
    }
    if (-not [string]::IsNullOrWhiteSpace($InputKind)) {
        $body.inputKind = $InputKind; $body.normX = $NormX; $body.normY = $NormY; $body.inputLabel = $InputLabel
    }
    try {
        $null = Invoke-RestMethod -Uri ($resolvedHub + "/api/v1/event") -Method Post -ContentType "application/json" -Body ($body | ConvertTo-Json -Compress) -TimeoutSec 2
        if ($null -ne $Stream -and $Stream.ControlPort -gt 0 -and $Type -ne "input" -and -not [string]::IsNullOrWhiteSpace($Message)) {
            $statusText = if ($StepId) { "[$StepId] $Message" } else { $Message }
            $null = Invoke-RestMethod -Uri ("http://127.0.0.1:{0}/status" -f $Stream.ControlPort) -Method Post -ContentType "application/json" -Body (@{ message = $statusText } | ConvertTo-Json -Compress) -TimeoutSec 1
        }
        return $true
    } catch { return $false }
}

function Stop-ExeboxDesktopStream {
    param($Stream)
    if ($null -eq $Stream) { return }
    $proc = if ($Stream -is [System.Diagnostics.Process]) { $Stream } elseif ($Stream.PSObject.Properties['Process']) { $Stream.Process } else { $null }
    if ($null -ne $proc) {
        try {
            if (-not $proc.HasExited) {
                $proc.Kill()
                $null = $proc.WaitForExit(2000)
            }
        } catch { }
        try { $proc.Dispose() } catch { }
    }
    if ($Stream.PSObject.Properties['ControlPortFile'] -and $Stream.ControlPortFile -and (Test-Path -LiteralPath $Stream.ControlPortFile)) {
        Remove-Item -LiteralPath $Stream.ControlPortFile -Force -ErrorAction SilentlyContinue
    }
}

# Backward-compatible shims for existing E2E scripts targeting the previous VLC UDP helper names
function Start-ExeboxLiveVlcStream {
    param(
        [int]$AutomationPort = 0,
        [string]$AutomationToken = "",
        [int]$TargetPid = 0,
        [string]$WindowTitle = "",
        [string]$SnapshotDir = "",
        [int]$UdpPort = 5004,
        [int]$Fps = 20,
        [string]$LogPath = "",
        [string]$HubUrl = "http://127.0.0.1:5005",
        [string]$Project = ""
    )
    $null = $UdpPort
    $resolvedProject = if (-not [string]::IsNullOrWhiteSpace($Project)) { $Project } elseif (-not [string]::IsNullOrWhiteSpace($WindowTitle)) { $WindowTitle } else { "Sandbox E2E" }
    $handle = Start-ExeboxDesktopStream -Project $resolvedProject -HubUrl $HubUrl -Fps $Fps `
        -AutomationPort $AutomationPort -AutomationToken $AutomationToken -TargetPid $TargetPid `
        -WindowTitle $WindowTitle -SnapshotDir $SnapshotDir -LogPath $LogPath `
        -StopOnTargetExit:($TargetPid -gt 0) -CompatFfmpegMarker
    if ($null -eq $handle) { return $null }
    return $handle.Process
}

function Stop-ExeboxLiveVlcStream {
    param($StreamProc)
    Stop-ExeboxDesktopStream -Stream $StreamProc
}
