# P2PTalk — Conversation Archive (Proof of Work)

Date: 12/09/2025

This document is a complete archive and summary of the multi-session work carried out on P2PTalk. It captures objectives, changes, builds, verifications, design review, and outcomes to serve as a proof of work record.

## Objectives
- Packaging hygiene: publish Release single-file and self-contained variants; disable Release logging.
- Trust UX: verified-only indicators, presence badges, and manual verification controls.
- Messaging features: edit/delete with signed frames; groundwork for delivery ACKs (opcodes 0xB3/0xB4).
- Startup/lock stability: fix lockscreen close behavior; guard passphrase handling.
- Build + checkpoint: Alpha Strike builds and manifests to detect regressions.
- Design: review Windows 11 Fluent guidance and prepare a minimal audit.

## Key Technical Stack
- .NET 9 + Avalonia 11, MVVM.
- Crypto: ECDH P-256 + HKDF-SHA256; AEAD transport; Ed25519 signatures.
- Opcodes: identity/avatar 0xA1/0xA2; chat 0xB0; edit/delete 0xB1/0xB2; ACKs 0xB3/0xB4; verification 0xC3–0xC5; presence 0xD0.

## Major Changes Implemented
- App bootstrap: only show `MainWindow` post-unlock; safe/normal startup stabilized.
- Unlock behavior: prevented reopening `MainWindow` when closing without unlock; persisted remember-passphrase.
- Resource fixes: added global `InverseBoolConverter`; removed runtime inclusion of deprecated ThemeSovereignty file.
- Logging policy: `Utilities/LoggingPaths.cs` disables logging for Release builds.
- UI polish: presence badges, verified shield, hover action bar for edit/delete, 24-minute edit window, delete confirmation.
- NAT and networking: delayed network init; UPnP mapping; discovery beacons; hairpin re-verification.

## Theming and OS Decoupling
- Central ordering in `App.axaml`:
  - `FluentTheme` → `ThemeSovereigntyCore.axaml` → `ThemeSovereigntyBase.axaml` → theme override (`Dark` by default) → `SharedTruncation.axaml`.
  - Set `RequestedThemeVariant="Dark"` in `App.axaml` to prevent OS theme changes from bleeding in.
- `ThemeService`:
  - Explicitly sets `Application.RequestedThemeVariant` when switching themes.
  - Adds a platform color suppression hook and reasserts theme on `PlatformSettings.ColorValuesChanged`.
- Propagated minimal UI updates across all themes (not just Dark):
  - Sovereign ToggleSwitch template (`PART_MovingKnobs`) with explicit brushes.
  - Message hover action bar fade-in rule.
  - Contacts list clears selected/focus backgrounds to avoid OS accent bleed.
  - Tab bottom accent indicator standardized (2px) across themes.
  - Icon button centering and fixed sizing.

Files touched:
- `App.axaml`
- `Styles/DarkThemeOverrides.axaml`
- `Styles/LightThemeOverrides.axaml`
- `Styles/ButterThemeOverride.axaml`
- `Styles/SandyThemeOverrides.axaml`
- `Services/ThemeService.cs`

## Build and Verification Pipeline
- Alpha Strike builds (Release + Debug, self-contained and single-file) executed multiple times.
- Publishing produced artifacts in `publish/` (Release zip ~11.73 MB; Release-sc ~45.93 MB; Release-single ~41.35 MB).
- Checkpoint verifier:
  - Detected style hash mismatches after theme updates (expected).
  - Regenerated manifest with `scripts/checkpoint-build.ps1` (Release, win-x64).
  - Subsequent `scripts/verify-checkpoint.ps1 -Strict` runs reported: “Checkpoint verified: no regressions in tracked files.”

Latest manifest:
- `publish/checkpoint-win-x64-Release-20250912-174545.json`
- `publish/latest-checkpoint.json` (copy of latest)

## Smoke Tests and Logs
- Safe-mode and normal Debug runs:
  - Validated startup, onboarding, and unlock flows.
  - Confirmed delayed network initialization, UPnP mapping, UDP discovery, hairpin reachable.
  - `app.log` tailed from `bin/Debug/net9.0/logs/app.log` with presence “Online.”

## Design Review (Windows 11)
- Reviewed Fluent principles; produced a minimal design audit checklist without code changes first.
- Proposed small, non-risky UI adjustments: visible and calm focus, spacing/density tweaks, safer accent usage, consistent geometry, optional materials guarded behind settings.

## Open Items / Next Steps
- UI surfacing for delivery ACKs and retry policy.
- Delete undo toast.
- Anti-replay window and canonical signing envelope for chat 0xB0.
- If approved, implement the minimal design audit items.

## Commands and Tasks (Executed Highlights)
- Alpha Strike (various profiles) and ad-hoc runs via tasks.
- `scripts/verify-checkpoint.ps1 -Strict` run repeatedly.
- `scripts/checkpoint-build.ps1 -Configuration Release -Rid win-x64` to refresh manifest.
- `scripts/kill-running.ps1` for prebuild hygiene; bin/obj cleans via PowerShell.

## Outcome
- Release artifacts current and reproducible; Release logging disabled.
- Styles unified and protected from OS overrides across all themes.
- Checkpoint manifest refreshed and verified clean.
- Startup and unlock flows stabilized; Debug smoke tests passed; networking validated.

---

This archive is intended as a durable proof of the work completed on 12/09/2025, consolidating the entire session’s context, changes, validations, and results.

## Chat History Summary
- Scope: packaging & release, trust UX, messaging edit/delete + ACK groundwork, lock/unlock stability, startup triage, theming sovereignty, pipeline & checkpoint, networking validation, and a Windows 11 design audit.
- Packaging/Build: Alpha Strike profiles implemented; Release single-file and self-contained published; Release logging disabled; artifacts stored under `publish/`.
- Trust UX: Verified shield applied to real contacts; presence badges added; manual verification entry points retained and clarified.
- Messaging: Edit/delete shipped with signed frames (0xB1/0xB2), 24-minute edit window, delete confirmation, hover action bar; groundwork added for delivery ACKs (0xB3/0xB4) and UI hooks.
- Stability: Lockscreen no longer reopens `MainWindow` on close; passphrase guarded; remember-passphrase persisted; only show `MainWindow` post-unlock.
- Startup: Safe-mode and normal paths validated; delayed network initialization confirmed.
- Theming & OS Decoupling: Sovereignty style ordering enforced; `RequestedThemeVariant` set; suppression of platform color changes via `ThemeService`; minimal UI tweaks propagated across Dark/Light/Sandy/Butter (sovereign ToggleSwitch, hover-bar fade-in, ContactsList background clears, tab underline, consistent icon buttons).
- Resources: Global `InverseBoolConverter` added; removed deprecated ThemeSovereignty runtime inclusion; defined `SystemAccentColor*` to prevent OS accent bleed.
- Networking: UPnP mapping, discovery multicast/broadcast, and hairpin reachability validated; presence reported “Online.”
- Checkpoint: Initial style hash mismatches expected; new checkpoint manifest published; strict verification now passes with no regressions.
- Design: Windows 11 audit prepared; minimal changes staged for future PR pending approval.
- Current State: Release artifacts current, checkpoint clean, Debug smoke tests green; next steps include delivery-ACK UI surfacing, delete undo toast, and anti-replay envelope.