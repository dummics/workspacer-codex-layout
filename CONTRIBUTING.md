# Contributing

Thanks for considering a contribution.

## Scope

This project is focused on:

- Codex-only window routing and layout behavior for Workspacer
- Windows 11 automation and recovery helpers around Workspacer
- local runtime hardening for problematic watcher behavior

Please avoid turning the repository into a generic tiling manager fork unless that change is explicitly discussed first.

## Development Notes

- the repository is local-first and Windows-focused
- build outputs, logs, and backups must stay untracked
- keep changes reviewable and easy to roll back
- prefer small, clear documentation updates when behavior changes

## Before Opening a PR

1. Verify the repository is clean with `git status`.
2. Make sure no logs, backups, or machine-specific state are staged.
3. Update `README.md` when user-facing behavior changes.
4. If you touch the watcher shim or startup/supervisor scripts, describe the validation you performed.

## Compatibility

The current config script assumes a standard Workspacer install that exposes `workspacer.Shared.dll` from the default install directory.
If you test against a non-standard install path, call that out clearly in the PR.
