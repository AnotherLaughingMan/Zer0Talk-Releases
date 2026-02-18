# Zer0Talk Install/Run Troubleshooting (Windows)

This guide is for users who report that Zer0Talk will not install or start on Windows.

## Most common cause
For alpha or unsigned builds, Windows Defender SmartScreen may block launch with:
- "Windows protected your PC"
- "unrecognized app"

This is expected for unsigned executables and does not always mean malware.

## What users should do (safe path)
Only continue if the file came from your official release page and filename/version match what you published.

1. In the SmartScreen window, click **More info**.
2. Confirm app name/path/version look correct.
3. Click **Run anyway**.
4. If prompted by UAC, click **Yes**.

If the user does not trust the source, they should click **Don't run**.

## If SmartScreen keeps blocking or deleting files
1. Open **Windows Security** -> **Virus & threat protection** -> **Protection history**.
2. Check whether Zer0Talk files were quarantined.
3. Restore only files that match your official release package.
4. Re-run installer/app from the official release download.

## If installer will not launch
1. Right-click installer -> **Properties**.
2. If present, enable **Unblock** and click **Apply**.
3. Try again.
4. If still blocked, use portable zip build and run `Zer0Talk.exe` directly.

## If app launches then exits immediately
1. Ensure they are using the x64 build on x64 Windows.
2. Ask user to run the self-contained package first (no runtime dependency).
3. Check logs under `%APPDATA%\Roaming\Zer0Talk\Logs\`.
4. Ask for the latest log file and Windows version.

## Quick response template for support
Use this message when users report install/run failure:

"This is usually Windows SmartScreen blocking unsigned alpha builds. Please re-download from the official release page, open the SmartScreen prompt, click More info, then Run anyway. If blocked again, check Windows Security Protection History and restore the app if it was quarantined. If needed, use the portable zip build and run Zer0Talk.exe directly."

## Maintainer fix (prevents most of these reports)
Long-term, sign release artifacts with Authenticode and timestamp them.

Recommended order:
1. Sign installer and primary executable during CI/release.
2. Timestamp signatures so they stay valid after cert expiration.
3. Keep publisher name stable across releases.
4. Publish SHA-256 checksums in release notes.

## Why this matters
Without code signing, many users will see SmartScreen prompts and some AV engines will raise reputation warnings. Signing does not guarantee zero warnings, but it significantly reduces install friction.
