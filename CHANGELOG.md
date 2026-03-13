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

### Removed
- **Private chat rooms / hosted server feature**: removed entirely from both the client and the relay server. The feature (persistent accounts, `ROOM-*` protocol, group key management, server-routed messaging, S2S federation) was out of scope for a P2P chat app and introduced unjustifiable infrastructure complexity and attack surface. All dedicated files (`RoomService`, `RoomKeyStore`, `RoomKeyCrypto`, `RoomsWindow`, `RoomsViewModel`, `ServerAccountService`, relay-side `HostedServerHost`, `RoomRepository`, `UserRepository`, `RoomFederationManager`, `ServerDatabase`, `RoomMessageQueue`) are deleted. Shared files (`AppSettings`, `AppServices`, `MainWindow`, `RelayConfig`, `RelayHost`, `RelayConfigStore`) are cleaned of all room/hosting references. `Microsoft.Data.Sqlite` and `Sodium.Core` packages removed from the relay server binary. `DataDirectory` config field is retained (used for the moderation audit log).

### Added
- **Blocked Users section in Settings → Network**: users can now manually block any peer by UID from the Settings panel — no longer requires the peer to be online in the Discovered Peers window. A text entry + Block button adds entries to `PeerManager`'s block list; a scrollable list shows all currently blocked peers with per-row ✕ remove buttons and a Clear All button. Entries persist across sessions via the existing block-list storage. `NetworkViewModel` gains `NewBlockedPeer` INPC property, `AddBlockedPeerCommand`, and `AddBlockedPeer()` method; `SettingsViewModel` proxies both to the outer surface.
- **Spoiler support in MarkdownParser / MarkdownRenderer pipeline**: `SpoilerInline` node added to `RenderModel`; `MarkdownParser.ConvertInline()` now uses `SpoilerTokenizer.Tokenize()` in the `LiteralInline` case to emit `SpoilerInline` nodes for `||text||` segments instead of a flat `TextInline`. `MarkdownRenderer.RenderInline()` handles `SpoilerInline` by rendering a black-on-black `Run` mask. `RenderParagraphAsTextBlock()` wires a `PointerPressed` handler that toggles all spoiler segments revealed/hidden by re-rendering from the model nodes — consistent with the legacy `ZTalkMarkdownViewer` spoiler path.
- **Stale TODO comment cleanup**: removed or replaced 7 stale TODO markers across `SettingsViewModel.cs`, `SettingsService.cs`, `Message.cs`, `MarkdownParser.cs` (HtmlInline dead case + image TODO), `ThemeEngine.cs` (FullEngine stub log), and `ThemeService.cs` (per-window override). All replaced with accurate annotations or removed outright.
- **Pinned Messages panel in chat**: a pin icon button (📌, no label) appears in the chat header next to the Burn Conversation button. Clicking it opens a collapsible panel above the chat canvas listing all messages pinned in the current conversation. Each entry shows the message content (1-line, truncated) plus an **Unpin** button — the second way to unpin alongside the existing action-bar toggle. The panel closes via the ✕ button or by clicking the pin icon again. `MainWindowViewModel` gains `IsPinnedPanelOpen`, `TogglePinnedPanelCommand`, `PinnedMessages`, `HasPinnedMessages`, and `RefreshPinnedMessages()` — called after every pin-toggle and timeline rebuild so the list stays current.
- **Glowing star badge on starred messages**: the star indicator shown next to a starred message's timestamp now displays in gold (`#FFD700`) with a golden drop-shadow glow effect, making starred messages immediately recognisable at a glance.
- **Starred messages section in Notification Dropdown → Messages tab**: opening the notification panel and switching to the Messages tab now shows a "Starred" section (below unread notices) listing all starred messages in the current conversation. Each entry has a gold glowing star, sender label, message preview, timestamp, and a one-click **Unstar** button — the second way to unstar alongside the action-bar toggle. Clicking an entry navigates to the conversation and closes the panel.
- **Action-bar tooltip clarity**: the Pin and Star hover-bar buttons now show `"Pin / Unpin"` and `"Star / Unstar"` as tooltips, surfacing their toggle nature.
- **Glowing pulsing unread badge on contact cards**: the unread message count pill now has a soft blue glow (`BoxShadow`) and a 1.5 s opacity pulse (full → 45% → full, infinite), making new-message indicators clearly visible at a glance. Implemented via the new `unread-badge` style class in `ThemeSovereigntyBase.axaml`. **Redesigned as a corner dot**: the number pill has been replaced by a 10 × 10 px glowing dot (`Border.unread-dot`, `CornerRadius=5`) overlaid in the absolute upper-right corner of the contact card via a `Panel` wrapper, with a 1.4 s accent-coloured glow + pulse animation. The dot is only visible when `HasUnread` is true, which is cleared automatically when opening the conversation (`LoadConversation` → `MarkConversationMessageNoticesRead`), so contacts currently being chatted with never show the dot.

### Updated
- **Search converted to floating dropdown card**: the always-visible search bar in the chat header is replaced by a magnifying-glass button (🔍) that opens a floating card overlay (`ChatSearchPopup`, `MinWidth="420"`, light-dismiss). The card contains the search `TextBox`, Previous / Next / Clear match buttons, result-status label, and conversation-filter `ComboBox`. No dedicated layout row is consumed; the chat canvas always fills the full available height.
- **Pinned Messages converted to floating dropdown card**: the inline pinned-messages panel that expanded inside the chat area — pushing the message canvas down — is replaced by a floating card overlay (`PinnedDropPopup`, `Width="360"`, `MaxHeight="320"`, light-dismiss) anchored to a dedicated pin 📌 button in the chat header. Clicking the pin icon toggles the card open/closed; the card shows the full `PinnedMessages` list with per-entry **Unpin** buttons and a header-row close button. The message canvas is never displaced.
- **Settings → Network → Detected Relays panel** — full rework: configured relay addresses now appear at the top with a **Config** badge, showing user-set DNS names (e.g. `zer0talk.duckdns.org:443`) instead of only raw IPs; virtual adapter IPs (VMware VMnet1/VMnet8, Hyper-V vEthernet, Loopback) and APIPA/link-local addresses (169.254.x.x) are filtered out of LAN beacon results; the RadioButton row is replaced with a compact **Show** label + ComboBox (All / Config / LAN / WAN); `HasDiscoveredRelays` now correctly hides the placeholder when entries are present (fixed PropertyChanged propagation from `NetworkViewModel` to `SettingsViewModel`).
- **Discovered Peers window — purpose-built real-time ViewModel**: the window now uses a new, self-contained `DiscoveredPeersViewModel` (`ViewModels/DiscoveredPeersViewModel.cs`) instead of borrowing a `NetworkViewModel` instance that was baked for the Settings panels. The new VM owns its own `Attach()` / `Detach()` lifecycle, subscribes directly to `SessionCountChanged` and `PeersChanged` on open/close, runs a 2 s byte-counter timer, and drives a `SyncPeers` diff against its own `ObservableCollection<Peer>`. All peer filtering, sorting, country-code derivation, and action commands (`Block`, `Unblock`, `Remove`, `Refresh`) live in the one file with no Settings coupling whatsoever. `DataContext` is assigned in code-behind at construction (not via an XAML inline instance), so Avalonia's DataContext timing issue is eliminated.
- **Discovered Peers window — live byte counters**: `AeadTransport` now tracks bytes written and read via `Interlocked.Add` counters exposed as `TotalBytesWritten` / `TotalBytesRead`. `NetworkService.GetSessionBytes(uid)` exposes per-session counters. The Peers model gains `BytesIn` / `BytesOut` INPC properties populated on each refresh. New **Bytes In** and **Bytes Out** columns in the Discovered Peers table show human-formatted values (B / KB / MB / GB) via the new `BytesFormatter` converter.
- **Discovered Peers window — LAN icon**: peers connected via a private LAN address (10.x, 172.16-31.x, 192.168.x, 169.254.x, loopback) now display a 🖥️ desktop computer emoji instead of a country flag or globe. The `Peer.IsLan` property (populated at refresh time) drives the branch. Tooltip text reads "Local Area Network".
- **Discovered Peers window — always-on live updates**: the window no longer requires the "Auto" checkbox to update. It now subscribes to `SessionCountChanged` (instant update on connect/disconnect) and `PeersChanged` (instant update on peer list changes), with a 2 s background timer used only for byte-counter polling. The Auto checkbox has been removed from the toolbar. Subtitle text updated to reflect the live behavior.
- **Discovered Peers window — column rework**: removed the Public Key and Node columns (which added visual clutter with no actionable value) and replaced them with a **Mode** badge ("Direct" / "Relay" / "—") showing how the app is currently connected to each peer. UID column is now stretch-width with ellipsis trimming. `Peer.ModeLabel` property added and wired from `GetConnectionMode` at each refresh cycle. `BytesFormatter` converter added to `Utilities` for future Rx/Tx display.
- **RelayForwarder buffer allocation**: relay pipe sessions now rent buffers from `ArrayPool<byte>.Shared` instead of allocating a new `byte[]` per session, eliminating per-session heap allocation and reducing GC pressure under load.
- **RelayForwarder write path**: removed redundant `FlushAsync` calls from the copy loop; `TCP_NODELAY = true` is already set on all relay sockets, so explicit flushes were no-ops.
- **Relay pair-wait EWMA** (client): initial relay pair-wait estimate lowered from 45 s to 20 s, cutting worst-case wait time on first relay connection.
- Settings → About → Privacy Policy button now opens the in-app `PrivacyPolicyDialog` instead of shell-opening `Disclaimer.md`. The dialog persists the user's acceptance and "do not show again" preference to `AppSettings`.
- Title bar drag-bar separator between the Logout button and the Minimize button replaced: the horizontal `<Separator/>` (which rendered incorrectly as a short horizontal bar) is now a `<Border Width="1" Height="18">` vertical rule, matching the visual intent.
- Settings panels (Appearance, Hotkeys, Debug, Performance, Accessibility, About, Network, Danger Zone, Logout) normalized to the `Settings > General` layout template: consistent `general-card` borders, `general-section-title` text style, and `general-divider` separators throughout. Panels that lacked a top-level section header (About, Danger Zone, Logout) now have one.
- Settings > Performance: removed Enforce RAM Limit and Enforce VRAM Limit UI controls. These settings are too coarse to be reliably beneficial and risk degrading performance on most systems; the underlying ViewModel/settings fields are preserved for compatibility.
- Settings > Performance > CPU: added guidance note to the CCD Affinity selector explaining that Auto is recommended for most users, and explaining that Zer0Talk's encryption/messaging workloads are cache-intensive so the 3D V-Cache die is the better target (which Auto already selects when detected).
- Settings > Performance > Simulate CCD Configuration (Debug only): card now uses half-width layout (`MaxWidth=340`, left-aligned) to reduce visual footprint.
- Settings > Performance > CPU: card narrowed to `MaxWidth=480` — content fits comfortably and the guidance note wraps earlier at the smaller width.
- Settings > Debug Tools > Log Maintenance: "Purge All Logs" now surfaces a settings-panel toast on success (`Purged N log file(s)`) and explicit failure (`Failed to purge logs: …`). Previously the toast was wired to an unbound property and silently discarded.
- Logging: presence-mode, presence-error, and presence-decision diagnostic entries rerouted from `ui.log` to `network.log`. UI.log is now used exclusively for visual/interaction events; presence state is a network-layer concern.
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
- **Message status stuck at "Sending" after peer comes back online**: `OutboxService.DrainAsync` successfully sent queued messages and removed them from the outbox but never advanced their `DeliveryStatus` from `Pending` ("Sending…" clock icon) to `Sent`. Root cause: no callback existed for the drain success path. Fixed by adding a `static event Action<string, Guid> OutboxService.MessageSentFromOutbox` fired after each successful chat-frame write; `MainWindowViewModel` subscribes to it and updates both the in-memory `Message` object and the persisted `.p2e` conversation file, exactly mirroring the existing `ChatMessageDeliveryAcked` (`Sent → Delivered`) path.
- **Unread badge never appeared on contact cards**: `AppServices.Notifications.NoticesChanged` fired every time a message notice was added or removed, but the handler only called `RefreshHasPendingInvites()` — it never called `RefreshContactMetadata()`. As a result `Contact.UnreadCount` was never updated when messages arrived (only on `SessionCountChanged` and `ContactManager.Changed`), so `HasUnread` was always false and the badge was always hidden. Added `RefreshContactMetadata()` to the `NoticesChanged` handler so badge counts update immediately when a new message notice lands.
- **Toggle Contacts button restored**: `ToggleLeftPanel_Click`, `CollapseBothInstant`, and `NormalizeCentralLayout` all guarded against `ColumnDefinitions.Count >= 6` — a stale check from when a right diagnostics panel occupied two additional `BodyGrid` columns. After the notification panel was converted to a dropdown, `BodyGrid` was reduced to 4 columns and the guard always failed, silently preventing the toggle from doing anything. All three functions updated to `Count >= 4`; references to the removed columns [4] and [5] removed.
- **Notification dropdown — entry width consistency**: Invite, Alert, and Security-Event entries in the notification dropdown now all stretch to fill the available card width (`HorizontalAlignment=Stretch`, `MinWidth=300`), matching the already-normalized Messages tab entries and eliminating narrow/misaligned cards across all three tabs.
- **Privacy Policy dialog — "View Full Policy on GitHub" URL corrected**: all references to the policy URL updated from `.../blob/main/PRIVACY-POLICY.md` to `.../blob/main/docs/PRIVACY-POLICY.md` to match the actual file location in the Releases repo. Fixed in `PrivacyPolicyDialog.axaml` (display text), `PrivacyPolicyDialog.axaml.cs` (`Process.Start` href), and both canonical-URL references inside `docs/PRIVACY-POLICY.md` itself.
- **Privacy Policy dialog — scroll area clipped the GitHub button**: the scrollable content `StackPanel` now has a 48 px bottom margin so the "View Full Policy on GitHub" button and URL can be scrolled fully into view above the footer stripe.
- **`RELAY-PEERS` includes federation-discovered relays**: clients requesting the `RELAY-PEERS` list now receive both statically configured peers *and* any relays discovered through the federation mesh, allowing client relay pools to grow transitively through the network without manual reconfiguration.
- **Pending session TTL extended to 90 s** (`RelayConfig.PendingTimeoutSeconds` default 60 → 90): gives slow or mobile peers more time to complete the relay pairing handshake before the pending slot is reclaimed.
- **Notification Drop Panel**: the Notifications panel has been converted from a side panel into a Discord-style dropdown card anchored to the bell icon in the title bar. Clicking the bell opens a floating card (340×480) with three tab buttons — Invites, Messages, and Alerts — at the top. The active tab is highlighted with the accent colour. Light-dismiss closes the panel. The right-column slot in the main layout has been removed, giving the chat area the full remaining width at all times.
- **Community relay list (Phase 3A)**: a curated list of community-maintained relay addresses is now baked into the app as an embedded resource (`Assets/Data/default-relays.json`). The Settings → Network → Relay section displays these entries in a new "Community Relays" card, each with a **Use** button that instantly sets the listed address as the primary relay. No manual copy-pasting required for first-time setup.
- **RELAY-PEERS gossip (Phase 3B)**: clients now send a `RELAY-PEERS` command to each connected relay after successful registration and merge the returned peer relay addresses into the candidate pool. This allows relay operators to advertise sibling relays via their `PeerRelays` config list, and new clients automatically discover additional endpoints without any manual configuration. Gossip runs in the background and does not block registration.
- **Contact-priority relay (Phase 4)**: the app now remembers which relay endpoint successfully connected you to each contact (`Contact.PreferredRelay`, persisted). On subsequent connection attempts the preferred relay is sorted to the front of the candidate list so the fastest proven path is always tried first. The preferred relay is updated automatically whenever a new relay session succeeds.
- **Relay audit fixes (Phase 0)**: `FederationSyncInterval` now respects `FederationSyncIntervalSeconds` from config instead of a hard-coded 60 s; dead `LookupAttemptsPerPeer` constant removed; `MaxFederationPeers` cap enforced in `ConnectToPeerAsync`; federated OFFER forwarding made fully awaitable (no more fire-and-forget); moderation actions now written to a persistent JSONL audit log (`relay-data/moderation-audit.jsonl`).
- **Cross-relay session bridging**: clients registered on different federated relay servers can now communicate transparently. When a session is queued and cannot be paired locally, the relay queries peer relays (RELAY-SESSION-QUERY), establishes a bidirectional TCP bridge (RELAY-BRIDGE), and pipes both clients' streams through it — end-to-end encryption is preserved.
- **Federation OFFER forwarding**: when an OFFER targets a UID not registered on the local relay, the relay automatically forwards the invite to the peer relay hosting that UID via RELAY-OFFER. Invites follow the user across federation boundaries.
- **Persistent inter-relay TCP connections**: relay federation health checks and directory sync now reuse a long-lived, auto-reconnecting TCP connection per peer relay (`PersistentFederationConnection`) instead of creating a new connection for every command, reducing federation overhead.
- **Parallel federation directory lookup**: user lookups across federated peers now use a fan-out/race pattern (`ParallelLookupAsync`) — all peers are queried simultaneously and the first hit wins, replacing the previous sequential scan.
- **Federation peer auto-reconnect**: when a federation peer fails 3+ consecutive health checks, the relay schedules a reconnect attempt (rate-limited to once per 60 s) rather than waiting for the next health-check cycle.
- **Happy Eyeballs relay candidate selection** (client): when connecting to a relay, candidates are tried in parallel with a 250 ms stagger between each. The first successful connection wins and all others are cancelled, cutting median relay connection time on multi-relay deployments.
- **LAN-discovered relays included in candidate pool**: relay servers discovered via UDP beacon (`RLY|token|port` broadcast) are now added to `BuildRelayCandidates()` alongside explicitly configured and gossip-discovered relays. The existing health-score + distance sort naturally ranks nearby LAN relays higher than remote ones.
- **Invite-source relay coordination**: `TryConnectViaRelayInviteAsync` now accepts the relay that delivered the POLL invite as an explicit `inviteSourceRelay` parameter. This relay is promoted to the front of the candidate list, ensuring Client B connects to the same relay where Client A is already queued — eliminating the coordination mismatch where both sides connected to different relays and never paired. The invite path also switches from a sequential fallback loop to Happy Eyeballs (parallel race with 250 ms stagger) for faster pairing.
- **Health score in relay token resolution** (client): relay candidates used for session token lookup are now sorted by health score (success rate / latency EWMA) in addition to distance, biasing resolution toward the healthiest relay.
- **New relay federation protocol commands**: `RELAY-SESSION-QUERY` (check if a pending session exists on a peer), `RELAY-BRIDGE` (claim a pending session and install a raw-pipe bridge from the peer), `RELAY-OFFER` (forward a rendezvous invite across relay boundaries).
- Privacy Policy dialog (`PrivacyPolicyDialog`): scrollable, in-app policy display with a "View Full Policy on GitHub" link, an "I have read and understand the Privacy Policy" acceptance checkbox (required before Close is enabled), and a "Do not show this dialog again" checkbox. Dialog is mandatory on first run (title-bar close and Escape blocked until accepted) and re-openable from Settings → About with full dismiss/re-accept control.
- Privacy Policy document (`docs/PRIVACY-POLICY.md`): authoritative policy covering data storage, identity, messaging, relay zero-knowledge guarantees, no-telemetry stance, intentionally absent features, data deletion, and legal compliance. Matches the in-app dialog content exactly.
- `AppSettings.PrivacyPolicyAccepted` and `AppSettings.DoNotShowPrivacyAgain` fields: persist first-run acceptance and do-not-show preference across sessions.
- First-run privacy policy show hook in `App.axaml.cs` (`ShowMainWindow`): after main window opens, checks `DoNotShowPrivacyAgain`; if false and window is not hiding to tray, shows the mandatory dialog and saves the result.
- Message delivery status indicators on outbound messages: clock (pending), single checkmark (sent), two gray checkmarks (delivered — peer device received via `0xB5` ACK), two accent-colored checkmarks (read — peer opened the conversation, `0xB7` read receipt received). Status persisted to disk and restored on conversation load. `Read` state added to `MessageDeliveryStatus` enum; `NetworkService` handles inbound `0xB7` frames and exposes `SendReadReceiptAsync`; `LoadConversation` fires best-effort read receipts to the peer for all their incoming messages on open. Delivery visual updated: a `Panel` with two conditional `TextBlock`s (gray for Pending/Sent/Delivered, accent-colored for Read) replaces the single static `TextBlock`.
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
- **Settings View performance: UI-thread disk I/O on every peer-list change** (`SettingsViewModel.cs` / `NetworkViewModel`): `RefreshLists()` contained three synchronous `Logger.Log()` file-write calls that executed on the UI thread (via `Dispatcher.UIThread.Post`) on every peer-list rebuild. Since `PeersChanged` triggered an unthrottled, unsuppressed `RefreshLists()` for every event, continuous peer activity caused repeated blocking file I/O inside Avalonia's render loop, producing visible jank immediately after opening the Settings panel. All three `Logger.Log` calls removed from `RefreshLists()`; two additional debug-log calls removed from the `PeersChanged` handler.
- **Settings View performance: `PeersChanged` fired `RefreshLists()` unthrottled on every event** (`SettingsViewModel.cs` / `NetworkViewModel`): the `PeersChanged` handler was posting `RefreshLists()` — a full LINQ sort + collection diff + per-peer country code lookup — directly to the UI thread with no rate-limiting, in addition to calling the throttled `NotifyNetworkStatus()` delegate. Under any rapid peer change (connect, disconnect, re-announce), every event caused an immediate full rebuild regardless of how fast events arrived. Fixed by routing `RefreshLists()` through a new `GetUiThrottled` gate (`NetworkViewModel.RefreshLists.throttle`, 500 ms debounce) so burst peer events collapse to a single rebuild per half-second.
- **Settings View performance: `RefreshDiscoveredRelays()` ran every 500 ms indefinitely** (`SettingsViewModel.cs` / `NetworkViewModel`): `_uiPulseHandler` (subscribed to the 500 ms `UiPulse` tick) called `RefreshDiscoveredRelays()` unconditionally on every tick for the entire lifetime of the app after Settings was first opened. `RefreshDiscoveredRelays()` queries LAN relay discovery, iterates relay config entries, and diffs an `ObservableCollection` — all at 2 Hz forever. Added a 5-second cooldown check (`_lastRelayRefresh` DateTime field) so relay discovery rebuilds fire at most once every 5 s.
- **Settings View performance: `NetworkViewModel` event subscriptions leaked across app lifetime** (`SettingsViewModel.cs`): `NetworkViewModel` previously had no `Dispose()` method — event subscriptions (`PeersChanged`, `UiPulse`) were only released in a GC finalizer, which could run arbitrarily late or never. `SettingsViewModel.Dispose()` did not call any cleanup on the inner `NetworkViewModel`. Replaced the finalizer with a proper `Dispose()` method on `NetworkViewModel` that unsubscribes `_peersChangedHandler` and `_uiPulseHandler` and unregisters both UI throttle keys; `SettingsViewModel.Dispose()` now calls `_networkVm?.Dispose()` via the backing field (avoids lazy-creating a VM just to dispose it).
- **WAN peer discovery: LOOKUP timeout too short for federated relay resolution** (`WanDirectoryService.Protocol.cs`): the client's per-relay read timeout for `LOOKUP` responses was 4 s, but a relay's own federated fan-out (`ParallelLookupAsync` → `QueryPeerForUserAsync` → `SendCommandAsync`) has its own 4 s timeout per peer. With TCP connection overhead the relay could legitimately take 4+ s to respond, causing the client to silently treat a valid federated hit as a miss. Timeout increased 4 s → 8 s.
- **Discovered Peers window: live updates broken after any peer is re-discovered** (`PeerManager.cs`, `DiscoveredPeersViewModel.cs`): `PeerManager.SetDiscovered` was replacing existing `Peer` objects with brand-new instances (`map[norm] = p`) instead of updating the existing objects in-place. The `DiscoveredPeersViewModel.Peers` `ObservableCollection` held references to the old objects; subsequent `Refresh()` calls fired INPC on the new objects (from `_peerManager.Peers`) that nothing in the UI was bound to — so all updates were silently discarded. Fixed by updating existing peer objects in-place in `SetDiscovered` (preserving object identity), plus a defensive check in `SyncPeers` that replaces any stale object reference when UIDs match but `ReferenceEquals` is false.
- **Discovered Peers window: Bytes In / Bytes Out columns always showed "0 B"** (`NetworkService.cs`, `Models/Peer.cs`): `GetSessionBytes` returned `(0L, 0L)` when no AEAD session existed for a peer, and `BytesFormatter` rendered `0L` as `"0 B"` — making peers with no session visually identical to peers with an active session that hasn't transferred data yet. Changed the no-session sentinel to `(-1L, -1L)`, which `BytesFormatter` correctly renders as `"—"`, clearly distinguishing "no session" from "session with no data". `Peer.BytesIn` / `Peer.BytesOut` default to `-1L` so the initial render shows `"—"` before the first `Refresh()` fires (previously defaulted to `0L`).
- **WAN peer discovery: outer LOOKUP cancellation window too short** (`NetworkService.cs`, `ContactRequestsService.cs`): both call sites wrapped `LookupPeerAsync` in a 5 s `CancellationTokenSource`. With the 4 s inner read timeout, a single slow relay consumed the entire outer window and suppressed all subsequent relay candidates. Outer window widened 5 s → 12 s to give sequential relay iteration enough time.
- **`ForceSeedBootstrap` toggle had no effect** (`WanDirectoryService.Protocol.cs`): `GetCandidateRelayEndpoints` had an empty `if (!settings.ForceSeedBootstrap && ...)` block — seeds were unconditionally appended regardless of the setting. Fixed: when `ForceSeedBootstrap=false` and an explicit relay is configured, seed nodes are omitted. When `ForceSeedBootstrap=true` or no explicit relay is set, seeds are included. The "Always use seed nodes" UI toggle now works as labeled.
- **Relay handshake TCP resource leak** — `TryConnectViaRelaySessionAsync` now calls `relayClient.Close()` in all three handshake-failure paths (parse failure, OperationCanceledException, general exception) before returning `false`. Previously the `TcpClient` was abandoned un-disposed; eventual GC collection would send a TCP RST, appearing on the relay side as a spurious stream error.
- **Relay 95-byte early-close bug**: the relay forwarder (`RelayForwarder.CopyAsync`) treated a graceful connection close after forwarding fewer than 100 bytes as a hard error, which happened to fire on every relay ECDH handshake (4-byte header + 91-byte P-256 SPKI = 95 bytes). This caused the relay to tear down both peer connections right as the handshake completed, leaving the waiting client with a `"closed early (95 bytes)"` log and a broken session. Fix: the early-close guard removed; graceful EOF now always results in a clean loop break.
- **Settings auto-save**: settings changes are now saved automatically 700 ms after the last property change. The "You Have Unsaved Changes" banner no longer requires a manual Save click; it still appears briefly and clears once the debounced save completes.
- **Discovered Peers — refresh button removed**: the ⟳ toolbar button has been removed. The window receives live updates via event subscriptions and a 2-second polling timer, so a manual refresh is redundant.
- **Discovered Peers window — selection lost on auto-refresh**: the 2 s timer now snapshots selected peer UIDs before triggering the collection diff and restores selection by UID after the refresh completes, so multi-selected rows are no longer deselected by every timer tick.
- **Monitoring graph — relay and outbound sessions now tracked**: the traffic graph previously showed all-zero rates for relay-connected and outbound sessions because the port-stats counter only covered inbound listener connections (via `CountingStream`). `GetAllSessionBytesSnapshot()` was added to `NetworkService`; the monitoring `Tick()` now sums plaintext bytes across every active `AeadTransport` session, computes a per-interval rate, and merges it into the TCP series, so the graph lights up for all connection modes.
- **Notification drop panel — Alerts not showing on first open**: two bugs caused alerts to appear blank until the user navigated away and back. (1) A null-dereference on `NotificationItem.Title` in the LINQ filter inside `RenderAlertsPanel` threw silently, short-circuiting the entire render. Fixed with a null-safe call (`Title?.Contains(...) == true`). (2) All three `Render*Panel` methods guard against the popup being closed, but the initial render call in `AttachRightPanelEventHandlers` ran *before* `popup.IsOpen = true`, so the guard killed it every time. `ToggleNotifDrop_Click` now renders the active panel immediately after setting `IsOpen = true`.
- **Relay restart leaves stale sessions**: `RelayHost.Stop()` now calls `RelaySessionManager.Reset()` (closes all active + pending sessions, clears recentFailures), clears the in-memory directory, clears `_invitesByTarget`, clears `_firstArrivalBySession`, and drains block lists. Prevents "already active" / "already queued" rejections immediately after an operator-initiated restart.
- Relay audit: `UnknownPendingTimeout` (20 s hard-coded) was evicting Phase-2 protocol sessions (UID-less `RELAY <sessionKey>`) before the client's adaptive pair-wait timeout (up to 60 s) expired, causing silent pairing failures. Timeout now always uses the operator-configured `PendingTimeoutSeconds` for all clients.
- Relay audit: client now logs specific, actionable messages for distinct relay `ERR` codes (`cooldown`, `rate-limit`, `blocked`, `capacity`, `already-active`, `already-queued`) instead of a single generic "Relay pairing failed" entry. `cooldown` and `rate-limit` responses also insert a 3.1 s back-off delay before returning to prevent immediate re-hammering.
- Relay audit: `CleanupLoopAsync` now logs cleanup exceptions instead of silently swallowing them; cleanup loop continues running regardless.
- Relay audit: TCP keepalive idle time, probe interval, and retry count now configured explicitly on all relay server and relay client sockets (30 s idle, 10 s interval, 3 retries) so half-open connections (remote crash without FIN) are detected in ≤60 s instead of the Windows OS default of 2 h.
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
