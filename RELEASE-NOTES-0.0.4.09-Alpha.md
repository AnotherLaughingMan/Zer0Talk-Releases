# Zer0Talk 0.0.4.09-Alpha

Release date: 2026-06-03

This alpha release focuses on stronger conversation security, clearer trust boundaries, better notification flow, and reliability improvements across contacts, verification, and updates.

## Highlights

- Forward secrecy improvements during live chats:
  Compatible peers now rotate transport keys during active sessions, reducing the value of any single long-lived session key.

- Explicit threat model and security boundaries:
  Added a dedicated threat model document that clearly explains what Zer0Talk protects, what it does not, and key operating assumptions.

- Reproducible build guidance:
  Added a practical rebuild-and-verify guide for users who want stronger software provenance checks.

- Better connection visibility:
  Added connection telemetry counters and relay health surfacing for improved monitoring.

- Better contact recovery and backups:
  Added local-source contact recovery plus rolling encrypted contact backups with retention controls.

- Better update experience:
  Added actionable update notices with install-now vs postpone flow and postpone windows.

- New built-in themes:
  Added Monokai Dimmed, High Contrast, High Contrast Dark, High Visibility, and Mercer Blue.

## Verification and security UX improvements

- Verification now uses a coordinated two-party ceremony window so stale intent cannot auto-complete later attempts.
- Added pending/cancel verification signaling so users see clearer live state.
- Verification event semantics are clearer across request/success/failure/cancel flows.

## Stability and behavior fixes

- Fixed premature verification dialog close edge cases.
- Restored expected main window dragbar behavior.
- Improved queued-delivery messaging accuracy.
- Hardened contact filtering fallback behavior.
- Improved timeout/cancellation handling in verification/update and relay-adjacent paths.

## Notes

- This remains an alpha build and may change quickly.
- Markdown support is temporarily removed in this release line.
- One non-blocking analyzer warning (`CA1305`) was observed during release publish in `App.axaml.cs` and did not block packaging.

## Docs

- Changelog: `CHANGELOG.md`
- Threat model: `THREAT-MODEL.md`
- Reproducible build guide: `REPRODUCIBLE-BUILD.md`
- Security policy: `SECURITY.md`
