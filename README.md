# Workspacer System

Codex-only window layout layer for [Workspacer](https://github.com/workspacer/workspacer) on Windows 11.

It manages only Codex windows (main + detached), leaves everything else untouched, and adds a local recovery/hardening runtime.

## Quick Start (2 minutes)

1. Install Workspacer (default path: `%ProgramFiles%\workspacer`).
2. Open PowerShell in this repo.
3. Run:

```powershell
. .\scripts\workspacer-tools.ps1
wsp watcher-fix
wsp start
wsp hardening-install
wsp status
```

If `wsp status` shows running + healthy, setup is complete.

## Installation

### Manual (copy/paste)

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
Set-Location "C:\path\to\workspacer-system"
. .\scripts\workspacer-tools.ps1
wsp watcher-fix
wsp start
wsp hardening-install
wsp startup-refresh
wsp status
```

### Agent install (Codex/LLM helper)

Use the prompt below with your agent.

<details>
<summary>Show agent prompt for full install and validation</summary>

```text
You are on Windows PowerShell. Configure this repo as a working Codex-only Workspacer runtime.

Repository path:
C:\path\to\workspacer-system

Rules:
- Use only commands needed for setup/validation.
- Do not edit files unless strictly required.
- Keep source Workspacer install untouched; operate through repo scripts/runtime.
- Stop and report clearly on first blocking error.

Steps:
1) cd to the repository path.
2) Dot-source scripts\workspacer-tools.ps1.
3) Run:
   - wsp watcher-fix
   - wsp start
   - wsp hardening-install
   - wsp startup-refresh
   - wsp status
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
wsp status
wsp restart
wsp recover
```

Essential commands:

- `wsp help`
- `wsp status`
- `wsp start`
- `wsp stop`
- `wsp restart`
- `wsp recover`

<details>
<summary>Show advanced commands</summary>

- `wsp hardening-install` / `wsp hardening-remove`
- `wsp watcher-fix` / `wsp watcher-restore`
- `wsp startup-refresh`
- `wsp supervisor-status` / `wsp supervisor-restart`
- `wsp tasks`

</details>

Hotkey: `Ctrl+F2` toggles Codex layout.

## Notes

- Source install default: `%ProgramFiles%\workspacer` (`WORKSPACER_SOURCE_DIR` to override)
- Local runtime default: `%LOCALAPPDATA%\Programs\workspacer-codex-runtime` (`WORKSPACER_RUNTIME_DIR` to override)
- Config mirror is kept synced to `%USERPROFILE%\.workspacer\workspacer.config.csx` for fallback compatibility.

## Disclaimer

Unofficial customization layer for Workspacer. Not affiliated with Workspacer or OpenAI.

## License

[LICENSE](LICENSE)
