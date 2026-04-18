Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'workspacer-tools.ps1')

Start-WorkspacerSupervisor | Out-Null
