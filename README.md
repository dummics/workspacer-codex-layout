# Workspacer System

Codex-only window layout layer for [Workspacer](https://github.com/workspacer/workspacer) on Windows 11.

It manages only Codex windows (main + detached), leaves everything else untouched, and adds a local recovery/hardening runtime.

## Quick Start (2 minutes)

1. Clone the repo to the canonical install path.
2. Run the installer script.

```powershell
$target = Join-Path $HOME '.config\workspacer-system'
git clone https://github.com/dummics/workspacer-codex-layout.git $target
powershell -ExecutionPolicy Bypass -File (Join-Path $target 'scripts\install-workspacer-system.ps1')
```

If `wsp status` shows running + healthy, setup is complete.

## Installation

### Manual (copy/paste)

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
$target = Join-Path $HOME '.config\workspacer-system'
if (-not (Test-Path $target)) {
    git clone https://github.com/dummics/workspacer-codex-layout.git $target
}
Set-Location $target
powershell -ExecutionPolicy Bypass -File .\scripts\install-workspacer-system.ps1
```

### Agent install (Codex/LLM helper)

Use the prompt below with your agent.

<details>
<summary>Show agent prompt for full install and validation</summary>

```text
You are on Windows PowerShell. Configure this repo as a working Codex-only Workspacer runtime.

Canonical repository path:
%USERPROFILE%\.config\workspacer-system

Rules:
- Use the canonical path above.
- Use only commands needed for clone/setup/validation.
- Do not edit files unless strictly required.
- Prefer Git as source of truth for install/update.
- Keep source Workspacer install untouched; operate through repo scripts/runtime.
- Stop and report clearly on first blocking error.

Steps:
1) Clone https://github.com/dummics/workspacer-codex-layout.git into %USERPROFILE%\.config\workspacer-system if missing.
2) cd to that path.
3) Run powershell -ExecutionPolicy Bypass -File .\scripts\install-workspacer-system.ps1
4) Dot-source scripts\workspacer-tools.ps1.
5) Run wsp status.
4) Return:
   - each command executed
   - final status summary (running/health/supervisor/task)
   - any warning and exact command to recover.
```

</details>

## Daily Usage

```powershell
. .\scripts\workspacer-tools.ps1
wsp help
wsp install
wsp status
wsp self-update
wsp restart
wsp recover
```

Essential commands:

- `wsp help`
- `wsp install`
- `wsp status`
- `wsp start`
- `wsp stop`
- `wsp restart`
- `wsp recover`
- `wsp self-update`

<details>
<summary>Show advanced commands</summary>

- `wsp hardening-install` / `wsp hardening-remove`
- `wsp watcher-fix` / `wsp watcher-restore`
- `wsp startup-refresh`
- `wsp supervisor-status` / `wsp supervisor-restart`
- `wsp tasks`

</details>

Hotkey: `Ctrl+F2` toggles Codex layout.

## Updates

From inside the canonical repo clone:

```powershell
. .\scripts\workspacer-tools.ps1
wsp self-update
```

`wsp self-update` requires a clean Git working tree and performs a fast-forward pull before reinstalling the runtime.

## Notes

- Source install default: `%ProgramFiles%\workspacer` (`WORKSPACER_SOURCE_DIR` to override)
- Canonical Git install root: `%USERPROFILE%\.config\workspacer-system`
- Local runtime default: `%LOCALAPPDATA%\Programs\workspacer-codex-runtime` (`WORKSPACER_RUNTIME_DIR` to override)
- `WORKSPACER_CONFIG` is pinned automatically to the repository root for deterministic loading.
- Config mirror is still synced to `%USERPROFILE%\.workspacer\workspacer.config.csx` as a fallback path.

## Disclaimer

Unofficial customization layer for Workspacer. Not affiliated with Workspacer or OpenAI.

## License

[LICENSE](LICENSE)
