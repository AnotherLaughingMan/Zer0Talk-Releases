# ZTalk Developer Guide

This guide summarizes the day-to-day commands, scripts, and conventions you need when working on ZTalk.

## Prerequisites

- Windows 10/11 x64
- .NET 9 SDK (preview is currently required)
- PowerShell 7+ (the automation scripts set ExecutionPolicy themselves)

## Common Commands

| Task | Command |
| --- | --- |
| Clean previous builds | `dotnet clean` |
| Debug build | `dotnet build --configuration Debug` |
| Release build | `dotnet build --configuration Release` |
| Run the app | `dotnet run --project .\P2PTalk.csproj` |
| Publish (Debug) | `pwsh -File scripts/publish-debug.ps1 -Configuration Debug -Rid win-x64` |
| Publish (Release single-file) | `pwsh -File scripts/publish-debug.ps1 -Configuration Release -Rid win-x64 -Single` |

> The build emits binaries to `bin/<Configuration>/net9.0/` and keeps publish artifacts under `publish/` (ignored by Git).

## Repository Hygiene

- The root `.gitignore` protects generated assets, logs, publish outputs, and tooling artifacts.
- If you need to track a resource under `Assets/`, add a `!` exception next to the existing rules.
- Remove previously tracked files by running `git rm --cached <path>` once the ignore pattern is in place.

## Code Quality & Diagnostics

- Analyzer warnings (e.g., CA1513, CA1816) surface during build—address them before pushing when practical.
- Logging lives under `bin/<Configuration>/net9.0/logs/`. Use `scripts/sample-logs.ps1` for quick triage.
- `Utilities/LoggingPaths` enumerates the expected log files; keep new log streams consistent with that class.

## Publish & Checkpoint Pipelines

| Script | Purpose | Notes |
| --- | --- | --- |
| `scripts/alpha-strike.ps1` | Full publish sweep (Debug/Release variants). | Supports `-IncludeDebugSingle` add-ons. |
| `scripts/checkpoint-build.ps1` | Generates checkpoint manifests and prunes previous zips. | Pair with `scripts/verify-checkpoint.ps1`. |
| `scripts/publish-debug.ps1` | Single RID publish (Debug or Release). | Accepts `-Single` and `-KeepZips` options. |
| `scripts/memory-profile.ps1` | Launches profiling build with logging helpers. | Useful during network stress tests. |

Run all scripts from the repo root; they handle `ExecutionPolicy` using `-ExecutionPolicy Bypass` automatically.

## Documentation Expectations

- Update `docs/architecture.md` when introducing a new service or component that affects data flow.
- Note new scripts or workflows here (`developer-guide.md`) so the rest of the team has a hub for operational changes.
- Root `README.md` should stay concise—link deeper explanations into the `docs/` folder instead of expanding the README endlessly.

## Git Identity

Set your identity before committing:

```powershell
git config user.name "Your Name"
git config user.email "you@example.com"
```

Use the `--global` flag to make this the default for all repositories on your machine.

## Issue Reporting

The project is pre-release. Avoid opening public issues unless the maintainers request them. Coordinate over the existing communication channels instead.
