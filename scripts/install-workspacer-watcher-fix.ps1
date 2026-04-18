param(
    [switch]$RestoreOriginal,
    [string]$SourceDirectory = '',
    [string]$TargetDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDirectory
$projectPath = Join-Path $repoRoot 'tools\watcher-shim\workspacer.Watcher.csproj'
$artifactRoot = Join-Path $repoRoot '.artifacts\watcher-shim\publish'
$backupRoot = Join-Path $repoRoot 'backups\watcher-fix'
$watcherFiles = @(
    'workspacer.Watcher.exe',
    'workspacer.Watcher.dll',
    'workspacer.Watcher.deps.json',
    'workspacer.Watcher.runtimeconfig.json'
)

if ([string]::IsNullOrWhiteSpace($TargetDirectory)) {
    $TargetDirectory = Join-Path $env:LOCALAPPDATA 'Programs\workspacer-codex-runtime'
}

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = if ([string]::IsNullOrWhiteSpace($env:WORKSPACER_SOURCE_DIR)) {
        Join-Path $env:ProgramFiles 'workspacer'
    }
    else {
        $env:WORKSPACER_SOURCE_DIR
    }
}

function Stop-WorkspacerProcesses {
    Get-Process workspacer, 'workspacer.Watcher' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

function Sync-WorkspacerRuntimeDirectory {
    if (-not (Test-Path $SourceDirectory)) {
        throw "Directory sorgente Workspacer non trovata: $SourceDirectory"
    }

    New-Item -ItemType Directory -Path $TargetDirectory -Force | Out-Null

    & robocopy $SourceDirectory $TargetDirectory /E /XO /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "Robocopy ha fallito sincronizzando il runtime locale di Workspacer. ExitCode=$LASTEXITCODE"
    }
}

function Get-LatestWatcherBackupDirectory {
    if (-not (Test-Path $backupRoot)) {
        return $null
    }

    Get-ChildItem -Path $backupRoot -Directory |
        Sort-Object Name -Descending |
        Select-Object -First 1
}

if ($RestoreOriginal) {
    $backupDirectory = Get-LatestWatcherBackupDirectory
    if (-not $backupDirectory) {
        throw "Nessun backup watcher trovato in $backupRoot"
    }

    Stop-WorkspacerProcesses

    foreach ($fileName in $watcherFiles) {
        $sourcePath = Join-Path $backupDirectory.FullName $fileName
        if (-not (Test-Path $sourcePath)) {
            continue
        }

        Copy-Item -Path $sourcePath -Destination (Join-Path $TargetDirectory $fileName) -Force
    }

    Write-Host "Watcher originale ripristinato da $($backupDirectory.FullName) su $TargetDirectory"
    return
}

if (-not (Test-Path $projectPath)) {
    throw "Project watcher shim non trovato: $projectPath"
}

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

Sync-WorkspacerRuntimeDirectory

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $artifactRoot `
    /p:UseAppHost=true | Out-Host

$backupDirectory = Join-Path $backupRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

Stop-WorkspacerProcesses

foreach ($fileName in $watcherFiles) {
    $installedFile = Join-Path $TargetDirectory $fileName
    if (Test-Path $installedFile) {
        Copy-Item -Path $installedFile -Destination (Join-Path $backupDirectory $fileName) -Force
    }
}

foreach ($fileName in $watcherFiles) {
    $publishedFile = Join-Path $artifactRoot $fileName
    if (-not (Test-Path $publishedFile)) {
        throw "Artifact watcher shim mancante: $publishedFile"
    }

    Copy-Item -Path $publishedFile -Destination (Join-Path $TargetDirectory $fileName) -Force
}

Write-Host "Watcher fix installato su $TargetDirectory. Backup creato in $backupDirectory"
