Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'workspacer-tools.ps1')

Register-WorkspacerHardening | Format-List TaskName, State
