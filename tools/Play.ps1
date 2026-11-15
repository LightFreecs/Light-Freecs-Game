# LightMMORPG launcher: rebuilds the Windows game when project files changed, then starts it.
# Used by the "LightMMORPG" desktop shortcut. Extra arguments are passed to the game (e.g. -touchui).

$GameArgs = @($args)
$ErrorActionPreference = 'Stop'
$Project   = Split-Path -Parent $PSScriptRoot
$Unity     = 'C:\Program Files\Unity\Hub\Editor\6000.5.1f1\Editor\Unity.exe'
$BuildDir  = Join-Path $Project 'Builds\Windows'
$Exe       = Join-Path $BuildDir 'LightMMORPG.exe'
$StampFile = Join-Path $BuildDir 'buildstamp.txt'
$LogFile   = Join-Path $BuildDir 'last-build.log'

$Host.UI.RawUI.WindowTitle = 'LightMMORPG Launcher'

function Get-SourceStamp {
    # Newest change time of anything that goes into the game.
    $newest = [datetime]::MinValue
    foreach ($dir in 'Assets', 'Packages', 'ProjectSettings') {
        $path = Join-Path $Project $dir
        if (-not (Test-Path $path)) { continue }
        $item = Get-ChildItem $path -Recurse -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if ($item -and $item.LastWriteTimeUtc -gt $newest) { $newest = $item.LastWriteTimeUtc }
    }
    return $newest.Ticks
}

function Test-ProjectOpenInEditor {
    $lock = Join-Path $Project 'Temp\UnityLockfile'
    if (-not (Test-Path $lock)) { return $false }
    try { $s = [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None'); $s.Close(); return $false }
    catch { return $true }
}

Write-Host ''
Write-Host '  Project LightMMORPG' -ForegroundColor Cyan
Write-Host '  Checking for changes...' -ForegroundColor Gray

$current = Get-SourceStamp
$built   = if (Test-Path $StampFile) { [long](Get-Content $StampFile -Raw).Trim() } else { 0 }
$needsBuild = (-not (Test-Path $Exe)) -or ($current -gt $built)

if ($needsBuild) {
    if (Test-ProjectOpenInEditor) {
        Write-Host '  The project is open in Unity (or another build is running), so it cannot be rebuilt right now.' -ForegroundColor Yellow
        if (-not (Test-Path $Exe)) { Read-Host '  No build exists yet. Close Unity and try again. Press Enter to exit'; exit 1 }
        Write-Host '  Starting the last build instead.' -ForegroundColor Yellow
    }
    else {
        Write-Host '  New changes found - updating the game. This takes a few minutes...' -ForegroundColor Yellow
        New-Item -ItemType Directory -Force $BuildDir | Out-Null
        $unityArgs = @('-batchmode', '-quit', '-buildTarget', 'Win64',
                  '-projectPath', "`"$Project`"",
                  '-executeMethod', 'LightMMORPG.EditorTools.ProjectBuilder.BuildWindowsPlayer',
                  '-logFile', "`"$LogFile`"")
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $p = Start-Process -FilePath $Unity -ArgumentList $unityArgs -PassThru -WindowStyle Hidden
        while (-not $p.HasExited) {
            Write-Host -NoNewline "`r  Building... $([int]$timer.Elapsed.TotalSeconds)s   "
            Start-Sleep -Seconds 2
        }
        Write-Host ''
        if ($p.ExitCode -eq 0 -and (Test-Path $Exe)) {
            # Stamp taken after the build, so files Unity touched while building don't trigger another rebuild.
            Set-Content -Path $StampFile -Value (Get-SourceStamp)
            Write-Host "  Updated in $([int]$timer.Elapsed.TotalSeconds)s." -ForegroundColor Green
        }
        else {
            Write-Host "  The build failed (exit code $($p.ExitCode)). Log: $LogFile" -ForegroundColor Red
            if (-not (Test-Path $Exe)) { Read-Host '  Press Enter to exit'; exit 1 }
            Write-Host '  Starting the last working build instead.' -ForegroundColor Yellow
            Start-Sleep -Seconds 3
        }
    }
}
else {
    Write-Host '  Already up to date.' -ForegroundColor Green
}

if ($env:LIGHTMMORPG_NOLAUNCH) { Write-Host '  (test mode: not launching)'; exit 0 }
Write-Host '  Launching...' -ForegroundColor Cyan
if ($GameArgs.Count -gt 0) { Start-Process -FilePath $Exe -ArgumentList $GameArgs -WorkingDirectory $BuildDir }
else { Start-Process -FilePath $Exe -WorkingDirectory $BuildDir }
