# Changelog

Note: project started approximately in May 2025 (date approximate).

Note: early project work (initial scaffolding and fast iteration) was not tracked in this changelog. Rapid scaffolding changes happened during the prototype phase and are intentionally omitted.

All notable changes to this project are recorded here. The format follows "Keep a Changelog" conventions and groups entries by release or date.

## Unreleased (2025-10-19)

### Added
- Theme Editor: Implemented path-based vector icons to remove a dependency on the previous icons package and improve scaling.
- Theme Engine: Added `ApplyThemePreview(ThemeDefinition)` to support live previews of color and gradient edits from the Theme Editor.
- Diagnostics: Added `LogResourceDiagnostics` (DEBUG-gated) to help trace resource application and investigate theme precedence issues.
- Documentation: `docs/FEATURE_LOG.md` added to capture design contract and implementation notes for the Theme Editor / Theme Engine work.

### Changed
- Theme Engine: `ApplyGradients` now removes existing gradient resources before adding new brushes and applies them to both the active window's `app.Resources` and `Application.Current.Resources` to ensure runtime precedence and immediate UI updates.
- Theme Service: Stop removing `App.TitleBarBackground` in legacy fallback paths to avoid overwriting runtime-applied gradients.
- Styles/AXAML: Removed hardcoded `App.TitleBarBackground` brushes from AXAML; titlebar gradients were migrated into `.zttheme` JSON theme files so themes are fully data-driven.
- Theme Editor UI: Fixed Color Picker layout (hex input repositioned, input sizing), Save/SaveAs flows, and duplicate theme ID handling.

### Fixed
- Resolved a regression where titlebar gradients required an application restart to take effect by fixing resource precedence and refresh logic.
- Fixed duplicate theme GUID handling and Save/SaveAs behaviors in the Theme Editor to prevent accidental overwrites.

### Notes
- Verbose diagnostic traces used during development are gated under `#if DEBUG` to keep release builds quiet.
- Commit: "Preserve runtime titlebar gradient and gate verbose theme diagnostics" (pushed to `origin/prototype`).

---

## 2025-10-05 — Critical: Markdown rendering crash fixed

### Fixed
- CRITICAL: Resolved an application crash on startup caused by corrupt or malformed markdown in message archives. Implemented multi-layer defenses in the markdown pipeline (viewer → parser → renderer) with graceful degradation and per-block isolation.

### Details
- Added `Zer0TalkMarkdownViewer` fallbacks and per-block error handling so malformed markdown no longer crashes the app. Errors are logged to `markdown-errors.log` for diagnosis.

## 2025-10-01 — Major networking & discovery fixes

### Fixed
- Peer list real-time updates: Fixed race condition in simultaneous peer connections that caused encryption/session failures.
- Version mismatch detection: Extended identity frames to include version metadata and added compatibility checks with user warnings.
- Simulated contacts: Filtered out simulated/test contacts from discovery lists to prevent UI confusion.

### Notes
- Changes include deterministic role resolution for simultaneous connections (ECDH public key comparison), extended identity frames to carry version information, and UI filtering logic for discovery lists.


## 2025-09-09 — Relay fallback (Delayed / Incomplete)

### Status
- Partially implemented but delayed: initial work added a relay fallback path and configuration keys, however integration testing, production configuration, and final QA were not completed. This feature remains incomplete and should not be considered released.

### Added (partial)
- Initial relay fallback implementation and settings were introduced (`AppSettings.RelayFallbackEnabled`, `AppSettings.RelayServer`). The implementation reuses the existing handshake and AeadTransport framing.

### Remaining work
- Integration tests for relay connectivity and failover paths.
- Production relay server configuration and documentation (how to provision `AppSettings.RelayServer`).
- End-to-end QA to verify fallback triggers correctly and does not regress direct connection flows.
- Logging/observability enhancements to monitor relay usage in staging/production.

### Notes
- Relay logs (when enabled) are written to `logs/network.log` but the overall feature should be considered in-progress until the remaining work is finished.

## 2025-09-07 — Misc fixes and UX guards

### Fixed / Improved
- Fix: Save toast overlays made Save appear disabled; toasts are now non-interactive to avoid intercepting clicks.

---

### How to use this changelog
- Add entries under "Unreleased" for in-progress changes; when a release is created, move entries into a dated or versioned heading and create a new "Unreleased" section.

If you want, I can commit this updated changelog and the feature log to `origin/prototype` (or create a separate PR). Otherwise the file is ready in the workspace.
