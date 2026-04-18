Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'workspacer-tools.ps1')

$registration = Register-WorkspacerHardening
$registration | Format-List TaskName, State
Write-Output "Suggerimento: usa 'wsp help' per i comandi essenziali del wrapper."
