Title: Evidence Locker - Secure diagnostic repurpose of the right-side panel

Status: Open
Priority: Medium
Estimate: 3 days
Labels: ui, security, design

Description

Repurpose the existing right-side Diagnostic Panel into an "Evidence Locker" feature. This is a secure, local-only store for artifacts (logs, message snapshots, peer metadata, screenshots, audio captures) that can be used for debugging and for sharing with peers during incident response.

Acceptance Criteria

- Right-side panel UI updated to show Evidence Locker view
- Ability to add artifacts via context menu (Save snapshot → add to Evidence Locker)
- Artifacts are encrypted at rest with local device key and optionally exportable (with user confirmation)
- Evidence Locker supports simple tagging, time-based filters, and a purge UI with strong confirmation

Notes

Sensitive data handling is required. Consider integrating with existing encryption utilities and maintaining strict local-first guarantees (no network sync unless explicitly exported by user).