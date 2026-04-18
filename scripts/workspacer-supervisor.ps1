param(
    [int]$PollSeconds = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'workspacer-tools.ps1')

$mutexName = 'Local\WorkspacerCodexSupervisor'
$createdNew = $false
$mutex = New-Object System.Threading.Mutex($true, $mutexName, [ref]$createdNew)

if (-not $createdNew) {
    return
}

$supervisorLogPath = Join-Path $script:WorkspacerSystemRoot '.config\workspacer\supervisor.log'
$crashDumpDirectory = Join-Path $env:LOCALAPPDATA 'CrashDumps'

function Write-SupervisorLog {
    param(
        [string]$Message
    )

    $directory = Split-Path -Parent $supervisorLogPath
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $timestamp = Get-Date -Format o
    "$timestamp pid=$PID $Message" | Add-Content -Path $supervisorLogPath
}

function Get-RecentWorkspacerEventSummary {
    param(
        [datetime]$Since
    )

    $events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $Since } -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProviderName -in @('.NET Runtime', 'Application Error', 'Windows Error Reporting') -and
            $_.Message -match 'workspacer|Watcher'
        } |
        Sort-Object TimeCreated -Descending |
        Select-Object -First 3

    if (-not $events) {
        return 'none'
    }

    return ($events | ForEach-Object { "$($_.TimeCreated.ToString('HH:mm:ss')):$($_.ProviderName):$($_.Id)" }) -join ' | '
}

function Get-NewCrashDumpSummary {
    param(
        [datetime]$Since
    )

    $dumps = Get-ChildItem $crashDumpDirectory -Filter 'workspacer*' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $Since } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 3

    if (-not $dumps) {
        return 'none'
    }

    return ($dumps | ForEach-Object { "$($_.LastWriteTime.ToString('HH:mm:ss')):$($_.Name)" }) -join ' | '
}

function Start-ManagedChild {
    Ensure-WorkspacerWatcherPatched
    Sync-WorkspacerConfigMirror
    Update-WorkspacerStartupShortcut

    $health = Get-WorkspacerHealth
    if ($health.RecommendedAction -eq 'restart') {
        Write-SupervisorLog "prestart-health reason=$($health.Reason) action=restart"
        Stop-WorkspacerManaged
        Start-Sleep -Milliseconds 800
    }

    $startTime = Get-Date
    $process = Start-Process -FilePath $script:WorkspacerExePath -WorkingDirectory $script:WorkspacerRuntimeInstallDir -PassThru
    Write-SupervisorLog "child-start pid=$($process.Id) exe=$script:WorkspacerExePath"

    [pscustomobject]@{
        Process = $process
        StartedAt = $startTime
    }
}

Write-SupervisorLog 'supervisor-start'

try {
    while ($true) {
        $child = Start-ManagedChild
        $process = $child.Process
        $startedAt = $child.StartedAt

        while ($true) {
            Start-Sleep -Seconds $PollSeconds

            $health = Get-WorkspacerHealth
            $reason = $health.Reason

            if ($reason -eq 'healthy') {
                continue
            }

            $observedAt = Get-Date
            $uptimeSeconds = [int][Math]::Round(($observedAt - $startedAt).TotalSeconds)
            $recentEvents = Get-RecentWorkspacerEventSummary -Since $startedAt.AddSeconds(-5)
            $recentDumps = Get-NewCrashDumpSummary -Since $startedAt.AddSeconds(-5)
            $exitCode = ''

            try {
                if ($process -and $process.HasExited) {
                    $exitCode = $process.ExitCode
                }
            }
            catch {
                $exitCode = 'unavailable'
            }

            if ($reason -eq 'watcher-missing' -or $reason -eq 'foreign-runtime-running') {
                Write-SupervisorLog "degraded pid=$($process.Id) reason=$reason uptimeSeconds=$uptimeSeconds forcing-stop=true recentEvents=$recentEvents recentDumps=$recentDumps"
                Stop-WorkspacerManaged
                Start-Sleep -Milliseconds 800
            }
            else {
                Write-SupervisorLog "child-exit pid=$($process.Id) reason=$reason exitCode=$exitCode uptimeSeconds=$uptimeSeconds recentEvents=$recentEvents recentDumps=$recentDumps"
            }

            break
        }

        Start-Sleep -Seconds 2
    }
}
finally {
    Write-SupervisorLog 'supervisor-exit'
    $mutex.ReleaseMutex() | Out-Null
    $mutex.Dispose()
}
