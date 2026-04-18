# Workspacer System

Unofficial Workspacer configuration and helper runtime for tiling only Codex windows on Windows 11.

This project is designed to:

- manage only top-level Codex windows
- include both the main Codex window and additional detached windows
- leave every non-Codex app unmanaged
- support multi-monitor routing without hardcoded screen order assumptions
- provide a local runtime hardening layer around Workspacer when needed

This repository is intended to be safe to keep local-first and later publish as open source.
It does not ship machine-specific backups, logs, or user profile data.

## Status

- local repository prepared for future public release
- no push is required yet
- the current runtime is still optimized for a single-user Windows workstation setup

## Disclaimer

This repository is an unofficial customization layer for [Workspacer](https://github.com/workspacer/workspacer).
It is not affiliated with or endorsed by the Workspacer project or by OpenAI.

## Repository Layout

- `.config/workspacer/workspacer.config.csx`: Codex-only Workspacer config
- `scripts/`: PowerShell and VBS helpers for startup, recovery, watchdog, and runtime install
- `tools/watcher-shim/`: patched watcher shim used to avoid problematic watcher behavior on some Windows 11 setups
- `backups/`: local-only non-versioned backups
- `.artifacts/`: local build outputs, not versioned

## Layout Behavior

- `0` Codex windows: no effect
- `1` Codex window on a monitor: the window stays effectively free / unsplit
- `2+` Codex windows on a landscape monitor: equal vertical columns
- `2+` Codex windows on a portrait monitor: equal horizontal rows
- the main Codex window is kept as the preferred left-most window when present
- non-Codex applications remain unmanaged

## Hotkey

- `Ctrl+F2`: toggle the Codex layout on or off

Default Workspacer quit/restart keybinds are explicitly disabled in this project because they are not needed for the Codex-only workflow.

## Paths and Configuration

This setup relies on the official `WORKSPACER_CONFIG` relocation behavior.

Important locations are resolved from environment variables whenever possible:

- Workspacer source install:
  - default: `%ProgramFiles%\workspacer`
  - override: `WORKSPACER_SOURCE_DIR`
- local runtime install:
  - default: `%LOCALAPPDATA%\Programs\workspacer-codex-runtime`
  - override: `WORKSPACER_RUNTIME_DIR`
- legacy config mirror:
  - default: `%USERPROFILE%\.workspacer\workspacer.config.csx`

The legacy mirror is kept in sync so Workspacer can still recover cleanly if it falls back to the historical `.workspacer` path.

## Config Script Compatibility

The tracked Workspacer config script currently references `workspacer.Shared.dll` from the default Workspacer install path.
On a standard setup this is expected to be `%ProgramFiles%\workspacer`.

If your Workspacer install lives elsewhere, adjust the first `#r` line in `.config/workspacer/workspacer.config.csx` before using the config.
This is the main remaining portability assumption in the repository.

## PowerShell Wrapper

The repository provides `scripts/workspacer-tools.ps1`, which exposes the `wsp` helper function.

Main commands:

- `wsp status`
- `wsp start`
- `wsp stop`
- `wsp restart`
- `wsp recover`
- `wsp tasks`
- `wsp install-hardening`
- `wsp remove-hardening`
- `wsp watcher-fix`
- `wsp watcher-restore`
- `wsp startup-refresh`
- `wsp supervisor-status`
- `wsp supervisor-restart`

Short aliases are also exposed for the same actions.

## Runtime Hardening

The repository includes a local runtime hardening layer:

- a silent supervisor process
- a scheduled task for logon, unlock, and periodic safety checks
- a watcher shim that can replace the stock `workspacer.Watcher.exe` inside the local runtime

The goal is to keep the user-facing Workspacer setup in the background and recover gracefully when the runtime degrades.

## Watcher Shim

`tools/watcher-shim/` contains a patched watcher replacement built as a windowless Windows executable.

The install script:

- clones the stable Workspacer install into a local runtime directory
- backs up the original watcher
- publishes the shim
- replaces the watcher only inside the local runtime, not the source install

Rollback is supported through `wsp watcher-restore`.

## Open Source Readiness

This repository is structured to be publishable later because:

- logs are ignored
- local build artifacts are ignored
- backups are ignored
- machine-specific runtime state is not versioned
- documentation avoids personal absolute paths

Before a public push, the recommended final check is:

1. verify `git status` is clean
2. verify no secrets or tokens exist in scripts or docs
3. decide the public repository name and description
4. review whether the Codex-specific naming should stay product-specific or become more generic

## Local Development Notes

For now this repository stays local-first.
When you are ready, it can be pushed to a public GitHub repository after a final packaging/review pass.

## License

See [LICENSE](LICENSE).
