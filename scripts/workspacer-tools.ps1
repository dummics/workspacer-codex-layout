Set-StrictMode -Version Latest

$script:WorkspacerSystemRoot = Split-Path -Parent $PSScriptRoot
$script:WorkspacerConfigRoot = $script:WorkspacerSystemRoot
$script:WorkspacerConfigEnvironmentVariableName = 'WORKSPACER_CONFIG'
$script:WorkspacerRepoConfigPath = Join-Path $script:WorkspacerSystemRoot '.config\workspacer\workspacer.config.csx'
$script:WorkspacerLegacyConfigPath = Join-Path $HOME '.workspacer\workspacer.config.csx'
$script:WorkspacerSourceInstallDir = if ([string]::IsNullOrWhiteSpace($env:WORKSPACER_SOURCE_DIR)) {
    Join-Path $env:ProgramFiles 'workspacer'
}
else {
    $env:WORKSPACER_SOURCE_DIR
}
$script:WorkspacerRuntimeInstallDir = if ([string]::IsNullOrWhiteSpace($env:WORKSPACER_RUNTIME_DIR)) {
    Join-Path $env:LOCALAPPDATA 'Programs\workspacer-codex-runtime'
}
else {
    $env:WORKSPACER_RUNTIME_DIR
}
$script:WorkspacerExePath = Join-Path $script:WorkspacerRuntimeInstallDir 'workspacer.exe'
$script:WorkspacerWatcherExePath = Join-Path $script:WorkspacerRuntimeInstallDir 'workspacer.Watcher.exe'
$script:WorkspacerEnsureScriptPath = Join-Path $PSScriptRoot 'ensure-workspacer.ps1'
$script:WorkspacerSupervisorScriptPath = Join-Path $PSScriptRoot 'workspacer-supervisor.ps1'
$script:WorkspacerSupervisorStarterScriptPath = Join-Path $PSScriptRoot 'start-workspacer-supervisor.ps1'
$script:WorkspacerSupervisorStarterVbsPath = Join-Path $PSScriptRoot 'start-workspacer-supervisor.vbs'
$script:WorkspacerLegacyLauncherScriptPath = Join-Path $PSScriptRoot 'start-workspacer-managed.ps1'
$script:WorkspacerWatcherFixScriptPath = Join-Path $PSScriptRoot 'install-workspacer-watcher-fix.ps1'
$script:WorkspacerEnsureLogPath = Join-Path $script:WorkspacerSystemRoot '.config\workspacer\ensure-workspacer.log'
$script:WorkspacerTaskName = 'Workspacer Ensure Codex Layout'
$script:WorkspacerStartupShortcutPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\workspacer.lnk'

function Get-WorkspacerConfigRootStatus {
    $currentProcessValue = [Environment]::GetEnvironmentVariable($script:WorkspacerConfigEnvironmentVariableName, 'Process')
    $currentUserValue = [Environment]::GetEnvironmentVariable($script:WorkspacerConfigEnvironmentVariableName, 'User')

    [pscustomobject]@{
        ConfigRoot = $script:WorkspacerConfigRoot
        ProcessValue = $currentProcessValue
        UserValue = $currentUserValue
        Matches = $currentProcessValue -eq $script:WorkspacerConfigRoot -and $currentUserValue -eq $script:WorkspacerConfigRoot
    }
}

function Ensure-WorkspacerConfigRoot {
    $changed = $false
    $currentProcessValue = [Environment]::GetEnvironmentVariable($script:WorkspacerConfigEnvironmentVariableName, 'Process')
    if ($currentProcessValue -ne $script:WorkspacerConfigRoot) {
        [Environment]::SetEnvironmentVariable($script:WorkspacerConfigEnvironmentVariableName, $script:WorkspacerConfigRoot, 'Process')
        $changed = $true
    }

    $currentUserValue = [Environment]::GetEnvironmentVariable($script:WorkspacerConfigEnvironmentVariableName, 'User')
    if ($currentUserValue -ne $script:WorkspacerConfigRoot) {
        [Environment]::SetEnvironmentVariable($script:WorkspacerConfigEnvironmentVariableName, $script:WorkspacerConfigRoot, 'User')
        $changed = $true
    }

    [pscustomobject]@{
        ConfigRoot = $script:WorkspacerConfigRoot
        Changed = $changed
    }
}

function Ensure-WorkspacerSourceInstalled {
    if (Test-Path $script:WorkspacerSourceInstallDir) {
        return $script:WorkspacerSourceInstallDir
    }

    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "Workspacer non trovato in $script:WorkspacerSourceInstallDir e winget.exe non disponibile."
    }

    & $winget.Source `
        install `
        --id=rickbutton.workspacer `
        -e `
        --accept-package-agreements `
        --accept-source-agreements | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "Installazione Workspacer via winget fallita. ExitCode=$LASTEXITCODE"
    }

    if (-not (Test-Path $script:WorkspacerSourceInstallDir)) {
        $installedCommand = Get-Command workspacer.exe -ErrorAction SilentlyContinue
        if ($installedCommand -and $installedCommand.Source) {
            $script:WorkspacerSourceInstallDir = Split-Path -Parent $installedCommand.Source
        }
    }

    if (-not (Test-Path $script:WorkspacerSourceInstallDir)) {
        throw "Workspacer risulta installato ma il path sorgente non e' stato risolto automaticamente."
    }

    return $script:WorkspacerSourceInstallDir
}

function Sync-WorkspacerConfigMirror {
    if (-not (Test-Path $script:WorkspacerRepoConfigPath)) {
        throw "Config repo non trovata: $script:WorkspacerRepoConfigPath"
    }

    $legacyDirectory = Split-Path -Parent $script:WorkspacerLegacyConfigPath
    if (-not (Test-Path $legacyDirectory)) {
        New-Item -ItemType Directory -Path $legacyDirectory -Force | Out-Null
    }

    if ((Test-Path $script:WorkspacerLegacyConfigPath) -and
        (Get-FileHash $script:WorkspacerRepoConfigPath).Hash -eq (Get-FileHash $script:WorkspacerLegacyConfigPath).Hash) {
        return
    }

    Copy-Item $script:WorkspacerRepoConfigPath $script:WorkspacerLegacyConfigPath -Force
}

function Get-WorkspacerProcess {
    Get-Process workspacer -ErrorAction SilentlyContinue
}

function Get-WorkspacerProcessInfo {
    Get-CimInstance Win32_Process -Filter "name='workspacer.exe'" -ErrorAction SilentlyContinue
}

function Get-WorkspacerWatcherProcessInfo {
    Get-CimInstance Win32_Process -Filter "name='workspacer.Watcher.exe'" -ErrorAction SilentlyContinue
}

function Get-WorkspacerManagedProcessInfo {
    Get-WorkspacerProcessInfo | Where-Object { $_.ExecutablePath -eq $script:WorkspacerExePath } | Select-Object -First 1
}

function Get-WorkspacerManagedWatcherProcessInfo {
    Get-WorkspacerWatcherProcessInfo | Where-Object { $_.ExecutablePath -eq $script:WorkspacerWatcherExePath } | Select-Object -First 1
}

function Get-WorkspacerSupervisorProcessInfo {
    Get-CimInstance Win32_Process -Filter "name='powershell.exe' or name='pwsh.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and $_.CommandLine.Contains($script:WorkspacerSupervisorScriptPath)
        } |
        Select-Object -First 1
}

function Get-WorkspacerWatcherBinaryInfo {
    if (-not (Test-Path $script:WorkspacerWatcherExePath)) {
        return $null
    }

    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($script:WorkspacerWatcherExePath)

    [pscustomobject]@{
        ExePath = $script:WorkspacerWatcherExePath
        FileVersion = $version.FileVersion
        ProductVersion = $version.ProductVersion
        FileDescription = $version.FileDescription
        Patched = ($version.ProductVersion -like '*codex-fix*') -or ($version.FileDescription -like '*Codex watcher shim*')
    }
}

function Get-WorkspacerTasks {
    Get-ScheduledTask -TaskName $script:WorkspacerTaskName -ErrorAction SilentlyContinue
}

function Install-WorkspacerWatcherFix {
    Ensure-WorkspacerSourceInstalled | Out-Null
    & $script:WorkspacerWatcherFixScriptPath -SourceDirectory $script:WorkspacerSourceInstallDir -TargetDirectory $script:WorkspacerRuntimeInstallDir
}

function Restore-WorkspacerWatcherOriginal {
    & $script:WorkspacerWatcherFixScriptPath -RestoreOriginal
}

function Ensure-WorkspacerWatcherPatched {
    $watcherInfo = Get-WorkspacerWatcherBinaryInfo
    if (-not $watcherInfo) {
        Install-WorkspacerWatcherFix | Out-Null
        $watcherInfo = Get-WorkspacerWatcherBinaryInfo
    }

    if (-not $watcherInfo) {
        throw "Watcher Workspacer non trovato nel runtime locale: $script:WorkspacerWatcherExePath"
    }

    if (-not $watcherInfo.Patched) {
        Install-WorkspacerWatcherFix | Out-Null
    }
}

function Update-WorkspacerStartupShortcut {
    $startupDirectory = Split-Path -Parent $script:WorkspacerStartupShortcutPath
    if (-not (Test-Path $startupDirectory)) {
        New-Item -ItemType Directory -Path $startupDirectory -Force | Out-Null
    }

    if (Test-Path $script:WorkspacerStartupShortcutPath) {
        Remove-Item -LiteralPath $script:WorkspacerStartupShortcutPath -Force -ErrorAction SilentlyContinue
    }

    $wsh = New-Object -ComObject WScript.Shell
    $shortcut = $wsh.CreateShortcut($script:WorkspacerStartupShortcutPath)
    $shortcut.TargetPath = 'wscript.exe'
    $shortcut.Arguments = "`"$script:WorkspacerSupervisorStarterVbsPath`""
    $shortcut.WorkingDirectory = $script:WorkspacerSystemRoot
    $shortcut.IconLocation = "$script:WorkspacerExePath,0"
    $shortcut.Save()
}

function Start-WorkspacerSupervisor {
    $supervisor = Get-WorkspacerSupervisorProcessInfo
    if ($supervisor) {
        return $supervisor
    }

    Ensure-WorkspacerConfigRoot | Out-Null
    Ensure-WorkspacerWatcherPatched
    Sync-WorkspacerConfigMirror
    Update-WorkspacerStartupShortcut

    $process = Start-Process -FilePath 'powershell.exe' `
        -ArgumentList @(
            '-NoLogo',
            '-NoProfile',
            '-WindowStyle', 'Hidden',
            '-ExecutionPolicy', 'Bypass',
            '-File', $script:WorkspacerSupervisorScriptPath
        ) `
        -WorkingDirectory $script:WorkspacerSystemRoot `
        -PassThru

    Start-Sleep -Seconds 2
    $supervisor = Get-WorkspacerSupervisorProcessInfo
    if ($supervisor) {
        return $supervisor
    }

    return $process
}

function Stop-WorkspacerSupervisor {
    $supervisor = Get-WorkspacerSupervisorProcessInfo
    if ($supervisor) {
        Stop-Process -Id $supervisor.ProcessId -Force
    }

    Stop-WorkspacerManaged
}

function Restart-WorkspacerSupervisor {
    Stop-WorkspacerSupervisor
    Start-Sleep -Milliseconds 800
    Start-WorkspacerSupervisor
}

function Get-WorkspacerHealth {
    $mainProcess = Get-WorkspacerManagedProcessInfo
    $watcherProcess = Get-WorkspacerManagedWatcherProcessInfo
    $allMainProcesses = @(Get-WorkspacerProcessInfo)
    $allWatcherProcesses = @(Get-WorkspacerWatcherProcessInfo)

    $foreignMainCount = @($allMainProcesses | Where-Object { $_.ExecutablePath -ne $script:WorkspacerExePath }).Count
    $foreignWatcherCount = @($allWatcherProcesses | Where-Object { $_.ExecutablePath -ne $script:WorkspacerWatcherExePath }).Count

    $reason = 'healthy'
    $recommendedAction = 'noop'

    if (-not $mainProcess) {
        $reason = 'main-missing'
        $recommendedAction = 'start'
    }
    elseif (-not $watcherProcess) {
        $reason = 'watcher-missing'
        $recommendedAction = 'restart'
    }
    elseif ($foreignMainCount -gt 0 -or $foreignWatcherCount -gt 0) {
        $reason = 'foreign-runtime-running'
        $recommendedAction = 'restart'
    }

    [pscustomobject]@{
        Healthy = ($recommendedAction -eq 'noop')
        Reason = $reason
        RecommendedAction = $recommendedAction
        MainProcess = $mainProcess
        WatcherProcess = $watcherProcess
        ForeignMainCount = $foreignMainCount
        ForeignWatcherCount = $foreignWatcherCount
    }
}

function Get-WorkspacerStatus {
    $configRoot = Get-WorkspacerConfigRootStatus
    $health = Get-WorkspacerHealth
    $process = $health.MainProcess
    $watcherProcess = $health.WatcherProcess
    $watcherInfo = Get-WorkspacerWatcherBinaryInfo
    $supervisor = Get-WorkspacerSupervisorProcessInfo
    $task = Get-WorkspacerTasks | Select-Object -First 1

    [pscustomobject]@{
        Running = $null -ne $process
        ProcessId = if ($process) { $process.ProcessId } else { $null }
        SourceInstallDir = $script:WorkspacerSourceInstallDir
        RuntimeInstallDir = $script:WorkspacerRuntimeInstallDir
        ExePath = $script:WorkspacerExePath
        ConfigRoot = $configRoot.ConfigRoot
        ConfigRootMatches = $configRoot.Matches
        ConfigRootProcessValue = $configRoot.ProcessValue
        ConfigRootUserValue = $configRoot.UserValue
        Health = $health.Reason
        SupervisorRunning = $null -ne $supervisor
        SupervisorProcessId = if ($supervisor) { $supervisor.ProcessId } else { $null }
        WatcherRunning = $null -ne $watcherProcess
        WatcherProcessId = if ($watcherProcess) { $watcherProcess.ProcessId } else { $null }
        WatcherExePath = if ($watcherInfo) { $watcherInfo.ExePath } else { $null }
        WatcherPatched = if ($watcherInfo) { $watcherInfo.Patched } else { $null }
        WatcherProductVersion = if ($watcherInfo) { $watcherInfo.ProductVersion } else { $null }
        ForeignMainCount = $health.ForeignMainCount
        ForeignWatcherCount = $health.ForeignWatcherCount
        RepoConfigPath = $script:WorkspacerRepoConfigPath
        LegacyConfigPath = $script:WorkspacerLegacyConfigPath
        EnsureTask = if ($task) { $task.TaskName } else { $null }
        EnsureTaskState = if ($task) { $task.State } else { $null }
        EnsureLogPath = $script:WorkspacerEnsureLogPath
    }
}

function Install-WorkspacerSystem {
    Ensure-WorkspacerConfigRoot | Out-Null
    Ensure-WorkspacerSourceInstalled | Out-Null
    Install-WorkspacerWatcherFix | Out-Null
    Update-WorkspacerStartupShortcut
    Register-WorkspacerHardening | Out-Null
    Start-WorkspacerManaged | Out-Null
    Start-Sleep -Seconds 2
    Get-WorkspacerStatus
}

function Update-WorkspacerSystemFromGit {
    $git = Get-Command git.exe -ErrorAction SilentlyContinue
    if (-not $git) {
        throw "git.exe non disponibile. Installa Git prima di usare self-update."
    }

    $insideWorkTree = & $git.Source -C $script:WorkspacerSystemRoot rev-parse --is-inside-work-tree 2>$null
    if ($LASTEXITCODE -ne 0 -or $insideWorkTree.Trim() -ne 'true') {
        throw "La cartella $script:WorkspacerSystemRoot non risulta un repository Git valido."
    }

    $dirty = & $git.Source -C $script:WorkspacerSystemRoot status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw "Impossibile leggere lo stato Git del repository installato."
    }

    if (-not [string]::IsNullOrWhiteSpace(($dirty -join '').Trim())) {
        throw "Il repository installato ha modifiche locali. Pulisci o committa prima di usare self-update."
    }

    & $git.Source -C $script:WorkspacerSystemRoot fetch origin main --prune | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Fetch remoto fallito durante self-update."
    }

    & $git.Source -C $script:WorkspacerSystemRoot pull --ff-only origin main | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Pull fast-forward fallito durante self-update."
    }

    $installerPath = Join-Path $script:WorkspacerSystemRoot 'scripts\install-workspacer-system.ps1'
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $installerPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Reinstall fallito dopo il self-update. ExitCode=$LASTEXITCODE"
    }

    Get-WorkspacerStatus
}

function Start-WorkspacerManaged {
    Start-WorkspacerSupervisor | Out-Null
    Start-Sleep -Seconds 2
    return Get-WorkspacerProcess | Select-Object -First 1
}

function Stop-WorkspacerManaged {
    $processes = Get-Process workspacer, 'workspacer.Watcher' -ErrorAction SilentlyContinue
    if ($processes) {
        $processes | Stop-Process -Force
    }
}

function Restart-WorkspacerManaged {
    Stop-WorkspacerManaged
    Start-Sleep -Milliseconds 700
    Start-WorkspacerManaged
}

function Invoke-WorkspacerRecovery {
    $supervisor = Start-WorkspacerSupervisor
    $health = Get-WorkspacerHealth

    if ($health.RecommendedAction -eq 'noop' -and $supervisor) {
        return [pscustomobject]@{
            Action = 'noop'
            HealthReason = $health.Reason
            ProcessId = if ($health.MainProcess) { $health.MainProcess.ProcessId } else { $null }
            Running = $true
        }
    }

    return [pscustomobject]@{
        Action = 'supervisor-started'
        HealthReason = $health.Reason
        ProcessId = if ($supervisor) { $supervisor.ProcessId } else { $null }
        Running = $null -ne (Get-WorkspacerProcess | Select-Object -First 1)
    }
}

function Register-WorkspacerHardening {
    $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $service = New-Object -ComObject 'Schedule.Service'
    $service.Connect()

    $rootFolder = $service.GetFolder('\')
    $task = $service.NewTask(0)

    $task.RegistrationInfo.Description = 'Ensures Workspacer is running for the Codex layout on logon, workstation unlock, and sparse safety checks.'
    $task.Settings.Enabled = $true
    $task.Settings.StartWhenAvailable = $true
    $task.Settings.AllowDemandStart = $true
    $task.Settings.DisallowStartIfOnBatteries = $false
    $task.Settings.StopIfGoingOnBatteries = $false
    $task.Settings.MultipleInstances = 2
    $task.Settings.ExecutionTimeLimit = 'PT2M'

    $task.Principal.UserId = $user
    $task.Principal.LogonType = 3
    $task.Principal.RunLevel = 0

    $logonTrigger = $task.Triggers.Create(9)
    $logonTrigger.Enabled = $true
    $logonTrigger.Delay = 'PT5S'
    $logonTrigger.UserId = $user

    $unlockTrigger = $task.Triggers.Create(11)
    $unlockTrigger.Enabled = $true
    $unlockTrigger.Delay = 'PT5S'
    $unlockTrigger.UserId = $user
    $unlockTrigger.StateChange = 8

    $heartbeatTrigger = $task.Triggers.Create(2)
    $heartbeatTrigger.Enabled = $true
    $heartbeatTrigger.StartBoundary = (Get-Date).ToString("yyyy-MM-dd'T'HH:mm:ss")
    $heartbeatTrigger.DaysInterval = 1
    $heartbeatTrigger.Repetition.Interval = 'PT15M'
    $heartbeatTrigger.Repetition.Duration = 'P1D'
    $heartbeatTrigger.Repetition.StopAtDurationEnd = $false

    $action = $task.Actions.Create(0)
    $action.Path = 'wscript.exe'
    $action.Arguments = "`"$script:WorkspacerSupervisorStarterVbsPath`""
    $action.WorkingDirectory = $script:WorkspacerSystemRoot

    $rootFolder.RegisterTaskDefinition($script:WorkspacerTaskName, $task, 6, $null, $null, 3, $null) | Out-Null

    Get-WorkspacerTasks
}

function Unregister-WorkspacerHardening {
    $task = Get-WorkspacerTasks
    if ($task) {
        Unregister-ScheduledTask -TaskName $script:WorkspacerTaskName -Confirm:$false
    }
}

function Show-WorkspacerHelp {
    @'
Workspacer wrapper (`wsp`)

Uso:
  wsp help
  wsp <comando>

Comandi essenziali:
  install             Setup completo out-of-the-box del runtime Codex.
  self-update         Aggiorna il repo installato via Git e riallinea il runtime.
  status              Mostra stato, health e percorsi runtime/config.
  start               Avvia Workspacer tramite supervisor.
  stop                Ferma processo Workspacer e watcher.
  restart             Riavvia Workspacer in modo gestito.
  recover             Avvia supervisor e prova recovery automatico.

Comandi avanzati:
  hardening-install   Registra la scheduled task di hardening.
  hardening-remove    Rimuove la scheduled task di hardening.
  tasks               Mostra dettagli della scheduled task.
  watcher-fix         Applica patch watcher nel runtime locale.
  watcher-restore     Ripristina watcher originale.
  startup-refresh     Rigenera shortcut Startup.
  supervisor-status   Mostra processo supervisor.
  supervisor-restart  Riavvia supervisor.

Note compatibilita':
  install-hardening e remove-hardening restano supportati come alias legacy.
'@.Trim()
}

function global:wsp {
    param(
        [Parameter(Position = 0)]
        [ValidateSet('help', '-h', '--help', '/?', 'install', 'self-update', 'status', 'start', 'stop', 'restart', 'recover', 'hardening-install', 'hardening-remove', 'install-hardening', 'remove-hardening', 'tasks', 'watcher-fix', 'watcher-restore', 'startup-refresh', 'supervisor-status', 'supervisor-restart')]
        [string]$Action = 'status'
    )

    switch ($Action) {
        'help' { Show-WorkspacerHelp }
        '-h' { Show-WorkspacerHelp }
        '--help' { Show-WorkspacerHelp }
        '/?' { Show-WorkspacerHelp }
        'install' { Install-WorkspacerSystem | Format-List }
        'self-update' { Update-WorkspacerSystemFromGit | Format-List }
        'status' { Get-WorkspacerStatus | Format-List }
        'start' { Start-WorkspacerManaged | Format-List Id, ProcessName, StartTime }
        'stop' { Stop-WorkspacerManaged }
        'restart' { Restart-WorkspacerManaged | Format-List Id, ProcessName, StartTime }
        'recover' { Invoke-WorkspacerRecovery | Format-List Action, HealthReason, ProcessId, Running }
        'tasks' { Get-WorkspacerTasks | Format-List TaskName, State, Author, Description }
        'hardening-install' { Register-WorkspacerHardening | Format-List TaskName, State }
        'install-hardening' { Register-WorkspacerHardening | Format-List TaskName, State }
        'hardening-remove' { Unregister-WorkspacerHardening }
        'remove-hardening' { Unregister-WorkspacerHardening }
        'watcher-fix' { Install-WorkspacerWatcherFix }
        'watcher-restore' { Restore-WorkspacerWatcherOriginal }
        'startup-refresh' { Update-WorkspacerStartupShortcut }
        'supervisor-status' { Get-WorkspacerSupervisorProcessInfo | Format-List ProcessId,ParentProcessId,ExecutablePath,CommandLine }
        'supervisor-restart' { Restart-WorkspacerSupervisor | Format-List ProcessId,ParentProcessId,ExecutablePath,CommandLine }
    }
}

function global:wsp-help { wsp help }
function global:wsp-install { wsp install }
function global:wsp-self-update { wsp self-update }
function global:wsp-status { wsp status }
function global:wsp-start { wsp start }
function global:wsp-stop { wsp stop }
function global:wsp-restart { wsp restart }
function global:wsp-recover { wsp recover }
