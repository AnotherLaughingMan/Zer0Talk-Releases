# Zer0Talk Workspace Audit

This document summarizes the project's scope, context, and a file-by-file inventory with concise roles to help onboard and align contributors.

## Context and scope

## Project Overview

Zer0Talk (formerly P2PTalk) is a secure peer-to-peer messaging application built with Avalonia UI and .NET 9. It features encrypted communication, contact management, identity verification, and a desktop interface using MVVM architecture. The app emphasizes local persistence, manual-save settings, and optional NAT traversal for connectivity.

Primary components:
- UI (Avalonia XAML) and ViewModels (MVVM)
- Services for identity, networking, NAT traversal, persistence, and theming
- Encrypted stores for account, settings, contacts, messages, and peers
- Overlay-based Settings with unsaved-changes flow and toast notifications

## Root
- Zer0Talk.sln — Solution file for the Zer0Talk project.
- Zer0Talk.csproj — Project configuration targeting net9.0 with Avalonia packages.
- Program.cs — Application entry point.
- StartupInit.cs — Initialization logic for startup.
- App.axaml — Avalonia Application XAML (theme resources include).
- App.axaml.cs — App bootstrap: unlock/account routing, theme apply, service wiring, delayed network init.
- app.manifest — Windows app manifest.
- README.md — Basic build/run notes.
- context.md — Conversation/context notes for this workspace.
- Audit.md — This audit document.
- Audit.txt — Previous audit notes/scratch (legacy).
- Directory.Build.props — Build properties for the project.
- GlobalSuppressions.cs — Global code analysis suppressions.
- lint-report.json — Lint report output.
- Zer0Talk.code-workspace — VS Code workspace configuration.
- .editorconfig — Editor configuration for code style.

## .github
- .github/copilot-instructions.md — Instructions for GitHub Copilot.

## .vscode
- .vscode/settings.json — VS Code settings for the workspace.
- .vscode/tasks.json — VS Code tasks configuration.

## AI
- AI/Archived/Coversation on 12-09-2025.md — Archived conversation notes.

## Assets
- Assets/Icons/Icon.ico — Main application icon regenerated from the PNG source.
- Assets/Icons/Icon.png — Main application icon in PNG format.
- Assets/Icons/Special/Encrypted.ico — Icon for encrypted items.
- Assets/Icons/Special/Verified.ico — Legacy verified shield (unused; replaced by vector control).
- Assets/Icons/Status/Away.ico — Status icon for away.
- Assets/Icons/Status/DND.ico — Status icon for do not disturb.
- Assets/Sounds/icq-music-on-startup.mp3 — Startup sound.
- Assets/Sounds/melodious-notification-sound.mp3 — Notification sound.

## Scripts
- scripts/update-icon.ps1 — Rebuilds `Icon.ico` from `Icon.png` using System.Drawing.

## Containers
- Containers/P2EContainer.cs — Encrypted container read/write (P2E format; upgrade-aware).
- Containers/MessageContainer.cs — Per-peer encrypted message store APIs.
- Containers/OutboxContainer.cs — Outbox message container.

## Controls
- Controls/AvatarImage.cs — Custom control for avatar images.
- Controls/VerifiedBadge.axaml — Vector verified shield badge (scales cleanly across UI).
- Controls/VerifiedBadge.axaml.cs — Code-behind for `VerifiedBadge` control.

## Models
- Models/AccountData.cs — Identity profile: username, UID, keys, display name, avatar/history.
- Models/AppSettings.cs — App settings (theme, retention, ports, window state, security prefs).
- Models/Contact.cs — Contact record with trust/verification flags.
- Models/ContactListHeader.cs — Header for contact list groups.
- Models/DisplayNameRecord.cs — Previous display names with timestamps.
- Models/Message.cs — Chat message DTO (content, timestamp, signature, keys).
- Models/Peer.cs — Peer record for discovery/known nodes.
- Models/QueuedMessage.cs — Queued message model.

## Services
- Services/AccountManager.cs — Load/save encrypted account data; presence checks and paths.
- Services/AppServices.cs — Global service locator and Passphrase; hooks event forwarding; UI pulse.
- Services/AvatarCache.cs — Avatar caching and invalidation events for UI.
- Services/ContactManager.cs — Contacts load/save and change events.
- Services/ContactRequestsService.cs — Handles contact request logic.
- Services/DialogService.cs — Confirmation/unsaved changes/passphrase dialogs.
- Services/DiscoveryService.cs — Peer discovery service.
- Services/EncryptionService.cs — Crypto utilities used across services.
- Services/EventHub.cs — Centralized app event aggregator (NAT/network/peers/UI pulses).
- Services/FocusFramerateService.cs — Manages framerate for focus.
- Services/IdentityService.cs — Identity state, profile fields, sign/verify helpers; change notifications.
- Services/LayoutCache.cs — Caches layout information.
- Services/LockService.cs — Locks the app, clears sensitive material.
- Services/NatTraversalService.cs — UPnP discovery/mapping, verification, change events.
- Services/NetworkService.cs — Listener, handshakes, transport, LAN beacons, send/receive chat; events.
- Services/OutboxService.cs — Manages outbox messages.
- Services/PeerCrawler.cs — WAN/seed discovery and updates to PeerManager.
- Services/PeerManager.cs — Peer collections, trust/block management, and list merges.
- Services/PeersStore.cs — Encrypted persistence for discovered peers.
- Services/RegressionGuard.cs — Guards against regressions.
- Services/RetentionService.cs — Cleanup runner (retention policies placeholder).
- Services/ScreenCaptureProtection.cs — Protects against screen capture.
- Services/SettingsService.cs — Encrypted settings load/save; DPAPI sidecar; unlock window state sidecar.
- Services/ThemeService.cs — Runtime theme switching by swapping style includes + Avalonia ThemeVariant.
- Services/UpdateManager.cs — UI timer utilities and throttling helpers.
- Services/VerificationService.cs — Identity/content verification helpers.
- Services/WindowManager.cs — Helper to show single-instance tool windows.

## Utilities
- Utilities/ErrorLogger.cs — Exception logging with source tags.
- Utilities/Logger.cs — Simple timestamped logger used by UI and services.
- Utilities/NetworkDiagnostics.cs — Counters and snapshot strings for diagnostics.
- Utilities/AeadTransport.cs — XChaCha20-Poly1305 framed transport.
- Utilities/Hkdf.cs — HKDF-SHA256 key derivation helper.
- Utilities/CountingStream.cs — Byte-counting stream wrapper.
- Utilities/UidToAvatarConverter.cs — XAML binding converter for avatar images by UID.
- Utilities/InverseBoolConverter.cs — XAML converter to invert bools.
- Utilities/UpnpClient.cs — UPnP abstraction for NAT.
- Utilities/GreaterThanZeroConverter.cs — Converter for greater than zero checks.
- Utilities/InteractionLogger.cs — Logs interactions.
- Utilities/LoggingPaths.cs — Paths for logging.
- Utilities/MessageSenderNameConverter.cs — Converter for message sender names.
- Utilities/NotOrConverter.cs — Logical NOT OR converter.
- Utilities/PresenceEqualsToBoolConverter.cs — Converter for presence equality.
- Utilities/RuntimeFlags.cs — Runtime flags.
- Utilities/StringEqualsMultiConverter.cs — Multi-converter for string equality.
- Utilities/StringNotNullOrEmptyConverter.cs — Converter for non-empty strings.
- Utilities/VerificationShieldVisibilityConverter.cs — Converter for verification shield visibility.

## Views (Windows and controls)
- Views/MainWindow.axaml — Main shell: left contacts, center chat, right diagnostics; Settings overlay.
- Views/MainWindow.axaml.cs — Window geometry cache, hotkeys, overlay logic, exit unsaved prompt.
- Views/NetworkWindow.axaml — Network/Peers/Major Nodes/Blocked/Logging/Adapters tabs UI.
- Views/NetworkWindow.axaml.cs — Topmost, drag bar, tab helpers; geometry persist.
- Views/MonitoringWindow.axaml — Live traffic and NAT status visualizations.
- Views/MonitoringWindow.axaml.cs — Updates and rendering orchestration; window state.
- Views/SettingsWindow.axaml — Legacy standalone settings (replaced by overlay for most flows).
- Views/SettingsWindow.axaml.cs — Standalone settings window geometry persistence.
- Views/UnlockWindow.axaml — Unlock UI before decrypting stores.
- Views/UnlockWindow.axaml.cs — Unlock flow; applies settings/theme; transitions to Main.
- Views/AccountCreationWindow.axaml — Onboarding to create account.
- Views/AccountCreationWindow.axaml.cs — Account creation and routing.
- Views/AddContactWindow.axaml — Add-contact dialog.
- Views/AddContactWindow.axaml.cs — Add-contact behavior.
- Views/LostPassphraseDialog.axaml — Dialog for lost passphrase.
- Views/LostPassphraseDialog.axaml.cs — Logic for lost passphrase dialog.
- Views/WindowBoundsHelper.cs — Ensures windows restore within visible screen bounds.

### Controls
- Views/Controls/SettingsView.axaml — Settings overlay content (Profile/General/Appearance/Security/Danger).
- Views/Controls/SettingsView.axaml.cs — Left menu wiring; panel switching; theme combo sync.
- Views/Controls/TrafficHistoryView.cs — Custom control drawing rolling traffic graphs.

## ViewModels
- ViewModels/MainWindowViewModel.cs — Contacts/messages bindings, send logic, diagnostics panel state.
- ViewModels/SettingsViewModel.cs — Manual-save settings VM (unsaved tracking, save/discard, toasts, backfill).
- ViewModels/MonitoringViewModel.cs — Tracks rates and history for monitoring.
- ViewModels/UnlockViewModel.cs — Unlock/passphrase handling and remember preference.
- ViewModels/AddContactViewModel.cs — Add contact validation and commands.
- ViewModels/AccountCreationViewModel.cs — Account creation wizard logic.

## Styles
- Styles/DarkThemeOverrides.axaml — Dark theme resources.
- Styles/LightThemeOverrides.axaml — Light theme resources.
- Styles/SandyThemeOverrides.axaml — Sandy palette resources.
- Styles/ButterThemeOverride.axaml — Butter palette resources.
- Styles/SharedTruncation.axaml — Reusable truncation/ellipsis styles.
- Styles/NeutralMetrics.axaml — Neutral metrics for styles.
- Styles/ThemeSovereigntyBase.axaml — Base theme sovereignty.
- Styles/ThemeSovereigntyCore.axaml — Core theme sovereignty.
- Styles/ThemeSovereignty.full.bak — Backup of full theme sovereignty.
- Styles/ThemeSovereignty.gone — Removed theme sovereignty.

## How the pieces fit
- Startup (App.axaml.cs) initializes libsodium, wires services and events, and routes to Account Creation or Unlock. After unlock, settings are loaded and the theme is applied on the UI thread. Networking starts after a brief delay and responds to configuration changes via EventHub.
- Settings follow a manual-save pattern: edits mark Unsaved; Save persists all categories (Profile, General, Appearance, Security) and applies the theme; Discard reverts staged edits. Closing Settings or the app prompts when unsaved.
- Persistence: `SettingsService` and `AccountManager` manage encrypted stores; passphrase can be remembered via DPAPI (Windows). Contacts, messages, and peers are also encrypted locally.
- Networking stack handles peer discovery (LAN + crawler), secure sessions, and message exchange; diagnostics and port mapping status are surfaced to the UI.

## Known areas to improve
- RetentionService: implement policy-driven cleanup.
- Expand tests/CI; currently build-only validation.
- Add telemetry-free crash reporting guidance in README.

## Recent changes and regression guards (2025-09-07)

- Fix: Save could only be clicked once when the "Saved" toast overlapped the Save button.
	- Change: Made the Save toast non-interactive (`IsHitTestVisible=False`) in `Views/SettingsWindow.axaml`.
	- Related: The inline Settings overlay toast (`Views/MainWindow.axaml`) and the Network window toast (`Views/NetworkWindow.axaml`) are also non-interactive.
	- Guard: Do not remove `IsHitTestVisible=False` from these toast overlays; otherwise they may intercept clicks and make Save appear disabled after the first click.

### New: Relay fallback connectivity (2025-09-09)

- What: Added an optional relay fallback path to establish sessions when direct TCP and UDP hole punching fail.
- Settings:
	- `AppSettings.RelayFallbackEnabled` (default: true)
	- `AppSettings.RelayServer` in the form `host:port` (e.g., `relay.example.com:443`). If blank/null, relay is skipped.
- Networking:
	- `NetworkService.ConnectWithRelayFallbackAsync(peerUid, hostOrIp, port, ct)` tries: direct → NAT punch → relay.
	- Reuses the same ECDH P-256 + HKDF handshake over the relay stream; AeadTransport framing unchanged.
	- Registers the session in `_sessions` and triggers the same presence/avatar bootstrap as direct connections.
	- `ContactRequestsService` now uses the new connect method, so contact handshakes benefit automatically.
- Logging:
	- Targeted connect path logs are written to `<exe>\\logs\\network.log` (e.g., success/fail, reason, server).
- Guards:
	- No changes to the existing handshake wire format.
	- Reader loop for relay uses a shared inbound dispatcher (`HandleInboundFrameAsync`) to avoid code drift.
	- NAT/UPnP behavior unchanged; relay is only attempted when enabled and configured.

## Total project file count (excluding /scripts, /publish, /bin, /obj, log files, and error.txt)

Total files: 122

Breakdown:
- C# files (.cs): 79
- XAML files (.axaml): 16
- Markdown files (.md): 6
- JSON files (.json): 2
- Other (icons, sounds, config): 19
