param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'workspacer-tools.ps1')

Install-WorkspacerSystem | Format-List
