# ZTalk

> **Alpha Warning:** ZTalk is now in an **InDev-Alpha** state. Updates land frequently, features shift under your feet, and plenty of things are in active repair at any given moment.

Peer-to-peer messaging experiment for Windows, built with Avalonia (.NET 9) and a service-heavy MVVM architecture. Everything runs locally: encrypted settings, identity sidecars, and contact data live on your machine—no central servers involved.

## Snapshot of the Current Feature Set

- **Peer-to-peer chat:** Direct messaging powered by the `NetworkService`, `PeerManager`, and `PeerCrawler` pipeline.
- **Identity & verification:** `IdentityService`, encrypted passphrases, verified badges, and presence avatars for each peer.
- **Rich presence:** Idle/DND awareness, grouped online/offline lists, and custom converters for avatars and status.
- **Encrypted storage:** `SettingsService` uses DPAPI passphrase sidecars; message containers keep history local.
- **Link intelligence:** `LinkPreviewService` fetches metadata in the background with throttled caching.
- **Lock screen & focus tools:** `LockService` overlays, blur gating, and `FocusFramerateService` for resource smoothing.
- **Theme swaps on the fly:** Palette overrides via `ThemeService` with dark/light accent variants.
- **Diagnostics baked-in:** Structured logging with rotation, checkpointable builds, and automation scripts for publishing.

## Known Issues & Issue Reporting

- Nearly every subsystem is mid-flight; regressions are expected and tracked internally.
- GitHub Copilot has already performed multiple audits—we’re aware of the major pain points.
- **Please don’t open new issue reports.** Doing so duplicates known bugs and slows development; we’ll announce when outside feedback is actionable again.

## Getting Started

```bash
# Build
dotnet build .\ZTalk.csproj

# Run
dotnet run --project .\ZTalk.csproj
```

### Requirements

- Windows 10/11 (x64) with the .NET 9 SDK installed.
- PowerShell 7+ for the automation scripts (they handle ExecutionPolicy themselves).

## Configuration & Data Paths

- Settings: `%AppData%\Roaming\ZTalk\settings.p2e` (encrypted at rest; auto-migrated from older paths).
- Logs: `bin/<Configuration>/net9.0/logs/` (see `Utilities/LoggingPaths`).
- Publish output: `publish/win-x64-<Variant>/` with timestamped zips.

## Build & Release Automation

- `scripts/alpha-strike.ps1` — multi-variant publish, optional single-file debug, retention management.
- `scripts/publish-debug.ps1` — fast Debug/Release publish with optional single-file bundles.
- `scripts/checkpoint-build.ps1` — generates checkpoint manifests and prunes older artifacts.
- `scripts/verify-checkpoint.ps1` — validates manifests; `-Strict` fails on mismatch.
- `scripts/memory-profile.ps1` & `scripts/memory-stress.ps1` — profiling helpers when tuning allocations.

## Documentation

- [Architecture Overview](docs/architecture.md) — how services, view models, and resources fit together.
- [Developer Guide](docs/developer-guide.md) — build commands, scripts, and repo hygiene tips.
- CODEOWNERS and a sensitive file audit workflow enforce reviews for critical assets; see the Developer Guide for details.

## Contributing

- Pull requests are welcome for targeted fixes and feature spikes, but expect fast-moving branches.
- Skip filing issues; channel feedback through direct discussion until the prototype stabilizes.

## License

This repository is currently closed-source for distribution purposes; reach out to the maintainers for reuse questions.
