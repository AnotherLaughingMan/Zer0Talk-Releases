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
- Message delivery status indicators on outbound messages: clock (pending), single checkmark (sent), filled checkmark (delivered via peer `0xB5` ACK). Status persisted to disk and restored on conversation load.
- `Settings > General` toggle: `Enable smooth scrolling` to control animated scrolling behavior in client log/chat surfaces.
- Relay settings toggle: `Enable smooth scrolling` to control animated scrolling in Relay Console and Probe Audit views.
- Client chat `Jump to latest` control now includes an unread counter badge when new messages arrive while scrolled away from bottom.
- Log Viewer `Jump to bottom` control now includes an unseen-entry counter badge while paused away from latest logs.
- Contact list selection indicator: theme-colored accent bar on the left edge of each contact card (visible on all items, highlighted on selected).
- Contact list search/filter bar: real-time text filtering by display name or UID above the contact list.
- Last message preview on contact cards: single-line truncated preview of the most recent message with timestamp.
- Unread message badge on contact cards: per-contact unread count pill (accent-colored) displayed on the right side.
- Connection mode indicator on contact cards: icon showing whether a peer is connected via direct (globe) or relay (sync) connection.
- Smart contact list sorting: contacts ordered by online presence first, then by last message time descending, then alphabetically.
- Verification history dialog: "View history" link on profile card opens a modal with full verification event details (replaces inline list).

### Updated
- Manual `Check for Updates` now posts an immediate `Checking for updates...` notice before network resolution and explicitly reports `You are already up to date` when no newer version is found.
- Client and Relay auto-follow/jump-to-latest flows now use eased smooth scrolling when enabled, with immediate snap fallback when disabled.
- Client main chat canvas (`ChatScroll`) keeps native mouse-wheel behavior (including flywheel/fast-scroll mice) and uses smooth scrolling only for programmatic follow/jump transitions.
- Client and Log Viewer jump controls are now icon-only (tooltip-first) and themed entirely through app resources for compatibility with built-in and custom themes.
- Added localization keys for jump tooltips (`MainWindow.JumpToLatest`, `LogViewer.JumpToBottom`) across shipped language files.
- Client wheel smooth scrolling now applies acceleration-based delta handling when enabled to preserve inertia wheel behavior while avoiding stutter/fight.
- Contact card "Encrypted" text badge replaced with a padlock icon glyph (MDL2 `E72E`) with tooltip, reducing visual noise.
- "Lock / Logout" title bar button renamed to "Logout" with a power button icon (MDL2 `E7E8`); updated across all 10 localization files.
- Contact list hover/selection/focus visuals fully purged of Fluent default blue; all states now use theme-owned resources (`App.Border`, `App.ItemHover`, `App.ItemSelected`, `App.Accent`).
- Added missing localization keys for new contact list UX features (`ViewVerificationHistory`, `SearchContacts`, `ConnectionDirect`, `ConnectionRelay`) and backfilled missing `StatusBar.*` keys across all 9 non-English locale files.
- Fluent `SelectionIndicator` and internal `Rectangle` elements hidden inside ContactsList `ListBoxItem` template across all themes via NeutralMetrics.

### Fixed
- Memory efficiency: bounded link preview cache growth with periodic TTL/size pruning to prevent long-session heap buildup from large numbers of unique preview URLs/images.
- Relay memory efficiency: added stale-entry pruning in relay rate limiter state tables so scanner/high-churn keys do not accumulate indefinitely.
- Relay memory efficiency: invite long-poll signal objects are now tracked with TTL and disposed during prune/UNREG/stop cleanup to prevent semaphore object buildup.
- Added explicit manual-update diagnostic breadcrumbs (`start`, `feed-unavailable`, `up-to-date`, `update-available`, `canceled`, `failed`) in `AutoUpdate` logs to make post-release updater triage faster.
- Fixed wheel-scroll fighting on high-inertia mice by removing forced animated wheel interception from chat scrolling.
- Fixed "Clear All Alerts" in Notification Center not clearing warnings and other alerts that have an origin UID (e.g. connection warnings).
- Fixed contact profile card showing "No verification events yet" after verification completes; profile now forces a full property refresh on open so verification history, fingerprint, and verified-on date always reflect current state.

## [0.0.4.04-Alpha] - 2026-03-04

### Added
- Theme Editor now includes a one-click `Normalize Themes` action to migrate on-disk custom/imported `.zttheme` files to the current compatibility key set.
- Debug-only markdown smoke test hotkey (`Ctrl+Alt+Shift+M`) in MainWindow to validate formatter actions against selected text and surface pass/fail quickly during QA.
- CI quality gate workflow (`.github/workflows/quality-gate.yml`) to enforce Debug/Release builds and relay federation smoke checks on `main` push/PR.
- New `Tests/Zer0Talk.Tests.csproj` xUnit test project with initial message-model regression coverage.
- New UID normalization utility (`Utilities/UidNormalization.cs`) with automated tests for prefix/case normalization.
- New outbox edit lifecycle tests covering dedupe, queued-content update, and cancel removal behavior.
- Origin-to-release promotion playbook (`docs/origin-to-release-promotion.md`) and helper script (`scripts/promote_origin_to_release.ps1`) to selectively cherry-pick validated commits into release.
- Added `scripts/list_promotion_candidates.ps1` to enumerate and copy `[promote]` candidate SHAs before release promotion.
- `scripts/promote_origin_to_release.ps1` now supports interactive candidate selection when `-CommitShas` is omitted.
- Conversation search v1 in chat header with in-thread query, next/previous match navigation, clear action, and active-match indicators.
- Reply workflow v1 with message-level reply action, composer reply chip, inline reply preview chips in timeline, and lightweight reply metadata transport.
- Contact-level notification policy toggles (mute/priority) from contact context menu with persistence in encrypted contacts storage.
- Encrypted backup/restore UX in Settings now exports `.ztbackup` archives with an embedded backup manifest and supports restore/import with overwrite confirmation.
- Message space organization controls in chat: per-message `Pin`/`Star` actions with persistent flags and saved views (`All`, `Unread`, `Mentions/Important`, `Attachments`).
- Multi-device migration bundle flow in Settings: one-time encrypted `.ztmigrate` export with transfer code ceremony and guided restore compatibility.
- Monitoring productization pass: user-facing health score and actionable `Connection Doctor` guidance in Monitoring window.
- Trust ceremony profile UX: contact fingerprint display, `Verified On` timestamp, and recent verification history entries.
- Re-verify flow in contact profile so users can perform a fresh ceremony without clearing prior trust state.
- Unicode emoji catalog resource (`Resources/Data/emoji-test.txt`) with loader/model utilities for category-driven reaction picker population.
- App-wide flag graphics resource pack (`Assets/Flags/*.png`, 246 embedded country/region flags) with manifest index (`Resources/Data/flags-index.json`) for reusable lookup.
- Message composer emoji picker integration as a shadowed glyph button with category-based emoji selection for inserting emojis into messages.
- EmojiOnlyFontSizeConverter utility for conditional emoji sizing (72pt for emoji-only messages, 16pt for mixed content).

### Updated
- Contacts list theming was centralized into sovereignty styles so hover/selected/focus visuals now follow theme resources consistently across all built-in themes.
- Theme save/export pipeline now auto-backfills required list/contact compatibility color keys (`App.ItemHover`, `App.ItemSelected`, `App.AccentLight`, border/list brush sync keys).
- Composer markdown row now includes an icon-only Fluent toggle to hide/show formatting buttons with tooltip guidance.
- AppServices critical outbox/remote-message guard paths now emit structured warning logs on failures instead of silently swallowing exceptions.
- Quality gate now runs automated tests and includes a PR guard that blocks newly introduced bare `catch { }` blocks in `App`/`Services`/`ViewModels` critical paths.
- Quality gate now uploads CI diagnostics artifacts (`.trx` test results and federation smoke log) for faster failure triage in GitHub Actions.
- Contact context menu notification actions are now state-aware (`Mute`/`Unmute`, `Mark as priority`/`Remove priority`) instead of generic toggles.
- Conversation search navigation now auto-scrolls to the active result when stepping through matches.
- Notification inbox ordering now prioritizes VIP and mention-triggered items ahead of standard notices.
- Toast notifications now include quick actions (`Go to Chat`, `Reply`, `Mute 1h`, `Mark read`) for message notifications.
- Incoming notification behavior now supports quiet-hours semantics with priority/mention bypass options.
- Verification dialog now includes side-by-side fingerprint compare guidance before confirming trust.
- Verification semantics were clarified in UI: shield badge indicates identity verification, while lock icon indicates encrypted transport state.
- Message hover reactions now use a compact single add-reaction button with a left-anchored flyout that keeps the action bar narrow.
- Reaction picker now renders grouped emoji categories from the Unicode catalog in a scrollable popup, including the standard Facebook/Twitter-style reaction set.
- Reaction picker now includes inline search to filter emoji/categories quickly in large catalogs.
- Reaction picker now supports category selection and only renders the active category grid (instead of all categories at once) to keep the flyout responsive on large emoji catalogs.
- Reaction picker now includes a skin tone selector for supported human/hand emojis.
- Reaction picker category selector now shows per-category icons for faster visual navigation.
- Reaction picker layout has been enlarged for readability (larger popup, larger controls, and 24px emoji glyphs with bigger hit targets).
- Reaction picker now scales to main-window size (responsive width/height caps) and uses fewer columns with larger cells to avoid off-window overflow and horizontal clipping.
- Reaction picker `Flags` category now renders real embedded colored flag graphics (image buttons) instead of relying on font-based regional indicator rendering.
- Reaction picker received layout tuning: narrower panel/cells to prevent horizontal overflow while searching, and adjusted flyout placement/offset so it opens closer to center instead of hugging the top edge.
- Reaction picker flyout placement is now adaptive by trigger-row position (top/middle/bottom), dynamically choosing top/bottom placement and offsets to better stay in-view and feel centered.
- Reaction picker adaptive placement now applies stronger center bias and explicit right/left edge overflow guards, with extra downward offset for top-row messages.
- Reaction picker positioning now anchors from the chat area left boundary (Discord-style), keeps the flyout clamped inside the main window, and uses steadier offsets across trigger rows.
- Reaction picker horizontal placement was further corrected to deterministic chat-left anchoring with stricter right-edge clamping, preventing overlap/off-window drift on right-side message rows.
- Reaction picker now uses a fixed panel width and a single deterministic chat-left placement lane, eliminating responsive width jitter and random horizontal drift that could push content off-window.
- Reaction picker popup constraints now use slide-only adjustment (no auto flip jitter) with a fixed 420px panel width for consistent Discord-style placement and no horizontal hunt behavior.
- Reaction picker placement was simplified to API-compatible deterministic offsets in a fixed chat-left lane, with auto-flip disabled and minimal top/bottom behavior to reduce jitter.
- Reaction picker implementation switched from per-message `Flyout` to a single shared `Popup` with explicit coordinates, fixed width, and stable chat-lane anchoring to prevent right-drift and random repositioning.
- Shared reaction picker popup now targets the root window coordinate space and uses explicit below-first / above-if-needed placement checks to prevent top-corner jumps and clipped headers.
- Shared reaction picker popup placement now resolves anchor coordinates into the chat canvas (`ChatScroll`) space and clamps width/position to that viewport so it stays over the actual message area.
- Reaction picker dropdown behavior now opens downward from the clicked message reaction button (no upward flip) over the chat canvas area.
- Reaction picker sizing was refined for usability: horizontal sizing was restored to the fixed lane behavior, and vertical sizing now keeps a stable usable dropdown height so the emoji grid does not collapse.
- Reaction picker vertical sizing logic was corrected to avoid open/click regressions: dropdown height is no longer forced to a fixed value and now caps to the available in-canvas space below the clicked reaction button.
- Reaction picker click handling now preserves the target message ID during popup close, fixing a race where clicking an emoji could close the picker without applying the reaction.
- Reaction picker vertical height behavior was stabilized to a consistent usable dropdown size (no aggressive below-space collapse), while preserving downward-open positioning and reaction click handling.
- Message reaction chips now render real flag images for country-flag reactions (with text fallback for non-flag emojis).
- Discovered peers list now renders real country flag graphics from `CountryCode` hints, with globe fallback when unavailable.
- Security settings blocked-IP and blocked-range lists now render real country flag graphics (with country-code fallback only when no flag asset is available).
- Added reusable flag rendering infrastructure for all views: `FlagImageCatalog`, `CountryCodeToFlagBitmapConverter`, and `Controls/FlagImage` to map country codes to real flag bitmaps.
- Reaction picker entries now expose tooltips with human-readable emoji names and country flag names/codes, and `Flags` search now matches country names (not just two-letter codes).
- Send button removed from chat composer in favor of implicit send-on-Enter/Shift+Enter behavior with emoji picker integration.
- Emoji-only messages now render at 72pt font size (increased from default 16pt) in both the markdown viewer and fallback text renderer for improved visibility.
- Message composer maximum height increased from 120px to 160px to better accommodate emoji input and multi-line composition.
- Reverify button in contact profile now appears when either `IsVerified` or `PublicKeyVerified` is true, enabling re-verification when contact keys change or verification needs renewal.
- Emoji catalog loader now loads only fully-qualified Unicode emoji variants (ignoring minimally-qualified duplicates) to eliminate redundant entries in picker categories.
- Relay connected-client operator view now defaults to privacy-safe handle-centric records, with optional sensitive fields controlled by relay config.
- Relay Console live log behavior now matches Probe Audit behavior with follow-latest state, unseen-entry tracking, and contextual Jump-to-latest tooltip text.
- Relay queue diagnostics now include first-arrival moderation handle, relay role, and repeated-first-arrival streak warnings to make pending-session root cause analysis faster.
- Relay Console now translates low-level relay transport messages into operator-friendly plain-English status lines, while Probe Audit retains protocol-level technical detail.


### Fixed
- Custom and imported `.zttheme` files now auto-normalize missing compatibility color overrides at load/register time, preventing fallback Fluent blue bleed in contact/list states.
- Added fallback `App.ItemHover`/`App.ItemSelected` resources in sovereignty base so older themes without explicit keys still render stable contact/list states.
- Composer markdown buttons (Bold/Italic/Underline/Strike/Quote/Code/Spoiler) now preserve highlighted text selection and correctly wrap selected text instead of replacing it with placeholder text.
- Floating markdown selection ribbon actions now apply reliably after selection, with focus/selection handling fixes that prevent no-op clicks.
- Underline markdown now uses `++text++` and renders as true underline (instead of `__text__`, which markdown interprets as bold).
- Underline is now available on the floating markdown toolbar and underline tooltips use the correct `++text++` syntax.
- Composer markdown tools visibility toggle now persists in settings across app restarts.
- Spoiler inline chips now align with surrounding text baseline instead of rendering slightly raised.
- Spoiler rendering now uses run-based inline text with message-level click-to-reveal/hide (instead of inline UI containers) for consistent baseline alignment.
- Spoiler rendering preserves exact spaces around spoiler boundaries in mixed markdown text.
- Unlock flow now shows a persistent warning notice when encrypted settings fail to decrypt and are auto-reset to secure defaults (instead of silent reset behavior).
- Reaction command parameter handling now accepts flyout context objects (not only raw message IDs), preventing binding failures in nested picker templates.
- Reaction picker search state now resets on open/close and the search box is focused on open for faster keyboard-driven picking.
- Missing `OrMultiConverter` resource registration in MainWindow.axaml that caused application startup crashes when accessing contact profile re-verification controls.
- Duplicate emoji entries in composer emoji picker (e.g., "Face in the Clouds" appearing twice) by filtering to fully-qualified Unicode variants only.
- Relay moderation controls now support handle-based operator blocking that immediately purges pending invites, disconnects active relay sessions for the blocked UID, and enforces blocks across directory commands.
- Relay OFFER/diagnostic logging now avoids exposing raw endpoint payloads by default, reducing sensitive operator-surface leakage.
- Relay session pairing now suppresses duplicate same-side arrivals for an already-pending session key (`ERR already-queued`) instead of replacing the waiting pending side, reducing queue churn when one peer repeatedly arrives first.
- Relay pending cleanup now expires unknown/anonymous pending sides faster, and stale pending entries are replaced during pairing attempts so disconnected ghost waiters stop lingering in queue.
- Client relay invite parser now correctly handles batched `INVITES` payloads when session keys contain `:` (for example `uidA:uidB`), preventing dropped invites and missing ACK/join behavior under repeated relay offers.

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
