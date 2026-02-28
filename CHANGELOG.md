# Changelog

All notable changes to Zer0Talk are documented in this file.

## How to use this file

1. Add new work under `## [Unreleased]` as you build it.
2. Keep entries grouped only under these headings:
   - `### Added`
   - `### Updated`
   - `### Fixed`
3. On release day, copy `Unreleased` into a version section and clear `Unreleased`.

## Pre-Release Checklist

Run this checklist before creating a release tag.

- [ ] Confirm `## [Unreleased]` contains all shipped work under `Added`, `Updated`, and `Fixed`.
- [ ] Verify app and relay versions are aligned in `Directory.Build.props` and installer display version is updated.
- [ ] Run local build validation (`dotnet: build`) and resolve blocking errors.
- [ ] Build release artifacts (zip + installer) and confirm expected file names/version.
- [ ] Ensure release assets include updater prerequisites: installer, release archive, and `update-manifest.json`.
- [ ] Verify `update-manifest.json` values: `version`, `tag`, `installerUrl`, `sha256`, and release notes URL.
- [ ] Publish/tag release in `AnotherLaughingMan/Zer0Talk-Releases` and confirm assets are attached.
- [ ] Move `Unreleased` notes into the new version section and reset `Unreleased` placeholders.

## [Unreleased]

### Added
- No unreleased additions yet.

### Updated
- No unreleased updates yet.

### Fixed
- No unreleased fixes yet.

## [0.0.4.03-Alpha] - 2026-02-28

### Added
- Security replay defense window for inbound signed chat/edit/delete frames (`0xB0`/`0xB1`/`0xB2`) to reject duplicate/replayed message IDs within a bounded TTL.

### Updated
- Link preview fetch pipeline now uses manual redirect handling with per-hop security checks instead of automatic redirect following.
- URL launcher now enforces an explicit `http/https` scheme allowlist before invoking OS handlers.
- Relay candidate selection now accepts saved/seed relays even when `RelayServer` is empty and adds fallback attempts on port `8443` when no explicit port is supplied.
- WAN directory registration now tracks auth tokens per relay endpoint (instead of a single global token) to improve multi-relay stability.

### Fixed
- Security audit remediation: SSRF in link previews was patched by blocking loopback/local/private/link-local/ULA targets (including DNS-resolved destinations) and validating every redirect hop before fetch.
- Security audit remediation: protocol-handler abuse from arbitrary URI launching was patched by rejecting non-`http`/`https` schemes at launch time.
- Security audit remediation: inbound replay acceptance of valid signed frames was patched with peer+frame+message-id dedup tracking and timed eviction.
- Security audit remediation: message-store tamper/corruption fail-open behavior was patched by quarantining unreadable conversation files and emitting explicit diagnostics.
- Security audit remediation: sensitive plaintext logging exposure was reduced by removing raw chat content logging and replacing it with metadata-only entries.
- Security audit remediation: update trust hardening was patched with trusted HTTPS host checks for manifest/installer sources and Authenticode verification before installer execution.
- Security audit remediation: remembered passphrase storage was hardened by adding DPAPI optional entropy, zeroing sensitive buffers, and migrating away from duplicate protected-blob storage in settings.
- Security audit remediation: passphrase export save flow on Windows now writes DPAPI-protected output by default instead of raw plaintext.
- Relay reliability remediation: relay fallback is no longer blocked by empty `RelayServer` when valid saved/seed endpoints exist.
- Relay reliability remediation: pending relay timeout defaults were raised to `60s` and migrated on load to align with client pair-wait windows.
- Relay reliability remediation: active sessions are no longer evicted by an aggressive 2-second age heuristic; only dead TCP sessions are replaced.
- Relay reliability remediation: unauthorized `POLL/WAITPOLL` no longer auto-register directory entries with port `0`; clients are required to re-register.
- Relay reliability remediation: relay host now applies an authenticated per-UID command limiter to reduce CGNAT false positives from IP-only throttling.

## [0.0.4.02-Alpha] - 2026-02-27

### Added
- Client auto-update system with manifest-first release discovery and GitHub release API fallback.
- Auto-update release automation workflow for build, packaging, manifest generation, and release upload.
- `Settings > General` toggle: `Allow Auto Updates`.
- `Settings > General` action: `Check for Updates Now` (manual check path).
- Update status display in Settings: `Last checked` timestamp.

### Updated
- Release pipeline updated to publish directly from `AnotherLaughingMan/Zer0Talk-Releases` using built-in workflow token.
- Installer/release flow aligned with updater requirements (`update-manifest.json` + installer + release archives).
- Version and build labeling aligned for `0.0.4.03-Alpha` across app/relay display surfaces.

### Fixed
- CI release build failures caused by missing ignored source files required for compile.
- Manual workflow dispatch tag resolution (avoids invalid branch-as-tag behavior).
- Auto-update controls now allow users to disable background checks while retaining explicit manual checks.
