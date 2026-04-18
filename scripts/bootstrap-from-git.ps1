param(
    [string]$RepositoryUrl = 'https://github.com/dummics/workspacer-codex-layout.git',
    [string]$InstallRoot = '',
    [string]$Branch = 'main'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Join-Path $HOME '.config\workspacer-system'
}

$git = Get-Command git.exe -ErrorAction SilentlyContinue
if (-not $git) {
    throw 'git.exe non disponibile. Installa Git prima di eseguire il bootstrap.'
}

$parentDirectory = Split-Path -Parent $InstallRoot
if (-not (Test-Path $parentDirectory)) {
    New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null
}

if (-not (Test-Path $InstallRoot)) {
    & $git.Source clone --branch $Branch --single-branch $RepositoryUrl $InstallRoot | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Clone fallito da $RepositoryUrl verso $InstallRoot"
    }
}
else {
    $insideWorkTree = & $git.Source -C $InstallRoot rev-parse --is-inside-work-tree 2>$null
    if ($LASTEXITCODE -ne 0 -or $insideWorkTree.Trim() -ne 'true') {
        throw "La cartella target esiste ma non e' un repository Git valido: $InstallRoot"
    }

    $dirty = & $git.Source -C $InstallRoot status --porcelain
    if (-not [string]::IsNullOrWhiteSpace(($dirty -join '').Trim())) {
        throw "La cartella target contiene modifiche locali. Pulisci il repository prima del bootstrap/update."
    }

    & $git.Source -C $InstallRoot fetch origin $Branch --prune | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw 'Fetch fallito durante il bootstrap.'
    }

    & $git.Source -C $InstallRoot pull --ff-only origin $Branch | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw 'Pull fast-forward fallito durante il bootstrap.'
    }
}

$installerPath = Join-Path $InstallRoot 'scripts\install-workspacer-system.ps1'
if (-not (Test-Path $installerPath)) {
    throw "Installer non trovato dopo il bootstrap: $installerPath"
}

& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $installerPath
if ($LASTEXITCODE -ne 0) {
    throw "Installazione finale fallita. ExitCode=$LASTEXITCODE"
}
