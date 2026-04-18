# Workspacer For Codex App For Windows

Dedicated [Workspacer](https://github.com/workspacer/workspacer) setup for **Codex App for Windows** on Windows 11.

What it does:

- manages only Codex App for Windows top-level windows
- includes the main window and detached/extra Codex windows
- leaves every non-Codex app untouched
- adds a local runtime, startup and recovery layer so the setup is reproducible

## Demo

![Codex Workspacer demo](assets/codex-workspacer-demo.gif)

## Install

Canonical install path:

```text
%USERPROFILE%\.config\workspacer-system
```

Run this:

```powershell
$target = Join-Path $HOME '.config\workspacer-system'
if (-not (Test-Path $target)) {
    git clone https://github.com/dummics/workspacer-codex-layout.git $target
}
powershell -ExecutionPolicy Bypass -File (Join-Path $target 'scripts\install-workspacer-system.ps1')
```

Install is complete when `wsp status` shows:

- `Running : True`
- `Health : healthy`
- `WatcherPatched : True`

## Use

```powershell
Set-Location (Join-Path $HOME '.config\workspacer-system')
. .\scripts\workspacer-tools.ps1
wsp help
```

Main commands:

- `wsp install`
- `wsp status`
- `wsp restart`
- `wsp recover`
- `wsp self-update`

Hotkey:

- `Ctrl+F2` toggles the Codex layout

<details>
<summary>Show advanced commands</summary>

```powershell
wsp start
wsp stop
wsp hardening-install
wsp hardening-remove
wsp watcher-fix
wsp watcher-restore
wsp startup-refresh
wsp supervisor-status
wsp supervisor-restart
wsp tasks
```

</details>

## Update

From inside the installed repo:

```powershell
. .\scripts\workspacer-tools.ps1
wsp self-update
```

`wsp self-update` requires a clean Git working tree and uses fast-forward only.

## Agent Prompt

Use this prompt with an agent if you want deterministic install/update from Git:

<details>
<summary>Show install prompt</summary>

```text
You are on Windows PowerShell. Install or update this repository as the dedicated Workspacer setup for Codex App for Windows.

Repository URL:
https://github.com/dummics/workspacer-codex-layout.git

Canonical path:
%USERPROFILE%\.config\workspacer-system

Rules:
- Use Git as the source of truth.
- Use exactly the canonical path above.
- Do not edit repository files unless strictly required by a real blocker.
- Keep the original Workspacer install untouched; use the repo runtime/scripts only.
- Stop immediately on the first blocking error and report it clearly.

Steps:
1. If %USERPROFILE%\.config\workspacer-system does not exist, clone the repository there.
2. If it already exists, verify it is a Git repository and do not overwrite local changes.
3. Run:
   powershell -ExecutionPolicy Bypass -File "%USERPROFILE%\.config\workspacer-system\scripts\install-workspacer-system.ps1"
4. Then run:
   - Set-Location "%USERPROFILE%\.config\workspacer-system"
   - . .\scripts\workspacer-tools.ps1
   - wsp status
5. Return:
   - every command executed
   - final `wsp status` output summary
   - whether install is healthy
   - exact recovery command if anything failed

Success criteria:
- Running = True
- Health = healthy
- WatcherPatched = True
- ConfigRootMatches = True
```

</details>

## Notes

- Source Workspacer install default: `%ProgramFiles%\workspacer`
- Canonical Git install root: `%USERPROFILE%\.config\workspacer-system`
- Local runtime default: `%LOCALAPPDATA%\Programs\workspacer-codex-runtime`
- `WORKSPACER_CONFIG` is pinned automatically to the repository root
- `%USERPROFILE%\.workspacer\workspacer.config.csx` is still kept as a fallback mirror

## Disclaimer

Unofficial customization layer for Workspacer, dedicated to Codex App for Windows. Not affiliated with Workspacer or OpenAI.

## License

[LICENSE](LICENSE)
