param(
    [string]$Reason = 'manual'
)

Set-StrictMode -Version Latest

$toolsPath = Join-Path $PSScriptRoot 'workspacer-tools.ps1'
. $toolsPath

$timestamp = Get-Date -Format o
$result = $null
$status = 'unknown'
$shouldLog = $false

try {
    $result = Invoke-WorkspacerRecovery
    $status = if ($result -and $result.Running) { 'running' } else { 'missing' }
    $shouldLog = $null -ne $result -and $result.Action -ne 'noop'
}
catch {
    $status = "error: $($_.Exception.Message)"
    $shouldLog = $true
}

$logDirectory = Split-Path -Parent $script:WorkspacerEnsureLogPath
if (-not (Test-Path $logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
}

if ($shouldLog) {
    $action = if ($result) { $result.Action } else { 'none' }
    $healthReason = if ($result) { $result.HealthReason } else { 'unknown' }
    "$timestamp reason=$Reason action=$action health=$healthReason status=$status" | Add-Content -Path $script:WorkspacerEnsureLogPath
}

if ($status -like 'error:*') {
    exit 1
}
