# Release Note: Windows Install/Run Advisory (SmartScreen)

## Summary
Some users may see Windows Defender SmartScreen warnings when launching Zer0Talk installer or executable builds.

This is primarily a trust/reputation prompt common for unsigned alpha builds.

## User Impact
- Installer may show "Windows protected your PC".
- Executable may be blocked or quarantined by Defender until user review.
- Support requests increase even though the app binary is otherwise functional.

## User Guidance
Direct users to:
- `docs/install-run-troubleshooting.md`

Quick guidance:
1. Confirm the file came from the official release page.
2. In SmartScreen, click **More info** -> **Run anyway**.
3. If blocked again, check **Windows Security -> Protection history**.
4. If needed, use the portable/self-contained package.

## Maintainer Actions
- Sign installer and executable with Authenticode.
- Timestamp signatures during release.
- Keep publisher identity consistent.
- Publish SHA-256 checksums with release artifacts.

## Status
This advisory is included to reduce install friction until full signing is in place.
