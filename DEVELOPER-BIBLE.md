# Zer0Talk — Developer Bible

**Version:** 0.0.4.05-Alpha  
**Last Audited:** 2026-03-10  
**Status:** Living document. Treat with the same seriousness as production code.

Every contributor is expected to read, understand, and follow this document. It defines what Zer0Talk is, why it exists, how it is built, and the principles that govern every decision made in this codebase.

---

## Table of Contents

1. [What Zer0Talk Is](#1-what-zer0talk-is)
2. [What Zer0Talk Is Not](#2-what-zer0talk-is-not)
3. [Repository Layout](#3-repository-layout)
4. [Technology Stack](#4-technology-stack)
5. [Cryptography and Security Model](#5-cryptography-and-security-model)
6. [Data Files and Storage](#6-data-files-and-storage)
7. [Startup Flow](#7-startup-flow)
8. [Service Layer](#8-service-layer)
9. [Connectivity Model](#9-connectivity-model)
10. [Network Protocol](#10-network-protocol)
11. [Relay Server](#11-relay-server)
12. [UI Architecture](#12-ui-architecture)
13. [Message System](#13-message-system)
14. [Version and Build System](#14-version-and-build-system)
15. [Testing](#15-testing)
16. [CI/CD](#16-cicd)
17. [Coding Standards](#17-coding-standards)
18. [Security Requirements](#18-security-requirements)
19. [Decisions and Policies](#19-decisions-and-policies)
20. [Changelog Discipline](#20-changelog-discipline)

---

## 1. What Zer0Talk Is

Zer0Talk is a **private, end-to-end encrypted peer-to-peer messaging application** for Windows. There is no central server that stores, routes, or mediates your messages. Every message is encrypted before it leaves the sender's machine and decrypted only on the recipient's machine. No third party — including relay operators — can read the content of conversations.

**Core properties:**

- **Keypair-based identity.** Each account is an Ed25519 keypair. Your UID is deterministically derived from your public key. There are no usernames or email addresses registered with any central authority.
- **Zero knowledge at relay.** Optional relay infrastructure can be self-hosted. Even when relay is used, the relay sees encrypted ciphertext only. The ECDH handshake and session keys are established end-to-end.
- **All data encrypted at rest.** Every persistent file is encrypted using XChaCha20-Poly1305 with Argon2id key derivation. Nothing is stored in plaintext.
- **Self-sovereign.** Users control their data directory, their keys, their relay, and their contacts. There is no account deletion form to fill out — you delete your `user.p2e` file.
- **Alpha stage.** The current version (`0.0.4.05-Alpha`) is pre-release. Breaking changes may occur between versions.

---

## 2. What Zer0Talk Is Not

These are explicit, decided policy rejections — not omissions:

| Feature | Status | Reason |
|---|---|---|
| File / image transfers | **Rejected** | CSAM/exploitation risk and reputation liability |
| AI-assist features | **Deferred** | Privacy trust model not yet defined |
| Cloud message backup | Not planned | Contradicts the zero-knowledge principle |
| SMS/email bridging | Not planned | Requires central account registry |
| Anonymous accounts | Not supported | UID is derived from keypair; no throwaway accounts |

---

## 3. Repository Layout

```
Zer0Talk/                          ← Main client (WinExe, .NET 9, Avalonia 11)
  App.axaml / App.axaml.cs         ← Application root
  Program.cs                       ← Entry point, single-instance guard, Avalonia init
  AppInfo.cs                       ← Version and build constants
  StartupInit.cs                   ← ModuleInitializer (earliest possible hook)
  Directory.Build.props            ← Shared version, analyzer policies
  GlobalSuppressions.cs            ← Global analyzer suppressions
  Assets/                          ← Icons, avatars, flags, sounds, syntax themes
  Containers/                      ← P2EContainer, OutboxContainer, MessageContainer
  Controls/                        ← Reusable Avalonia controls (AvatarImage, FlagImage, etc.)
  Models/                          ← Data models (no business logic)
  Resources/                       ← Localization JSON, emoji catalog, flag index
  Scripts/                         ← PowerShell dev/ops scripts (not compiled)
  Services/                        ← All business logic (singletons via AppServices)
  Styles/                          ← Avalonia theme AXAML
  Tests/                           ← xUnit test project
  Utilities/                       ← Pure helpers: logging, crypto, converters, paths
  ViewModels/                      ← MVVM view models
  Views/                           ← Avalonia windows and panels
  Zer0Talk.RelayServer/            ← Relay server (separate WinExe, same solution)
```

**Rule:** Do not put business logic in `Models/`. Do not put UI code in `Services/`. The boundary is strict.

---

## 4. Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 9 |
| UI framework | Avalonia 11.3.7 (Fluent theme) |
| UI icons | Material.Icons.Avalonia |
| Markdown | Markdig 0.37 |
| Symmetric crypto | libsodium (Sodium.Core) — XChaCha20-Poly1305 |
| KDF | Argon2id (Konscious.Security.Cryptography) |
| Signing | Ed25519 via libsodium |
| KDF (transport) | HKDF-SHA256 (custom `Utilities/Hkdf.cs`) |
| ECDH | P-256 (System.Security.Cryptography) |
| At-rest passphrase protection | DPAPI (`System.Security.Cryptography.ProtectedData`) |
| Audio | NAudio + NAudio.Vorbis |
| Reactive | System.Reactive 5.0 |
| Serialization | System.Text.Json (source-gen defaults, `SerializationDefaults.Indented`) |
| Test framework | xUnit 2.9 |
| OS-level APIs | System.Management, P/Invoke (Windows only) |

**Target OS:** Windows only. The `ApplicationManifest`, `WindowsFirewallRuleManager`, DPAPI, and `System.Windows.Forms.NotifyIcon` tray integration tie to Windows.

---

## 5. Cryptography and Security Model

This section is mandatory reading. Any change to cryptographic code requires deliberate review.

### 5.1 Identity (Ed25519)

Every account is an Ed25519 keypair generated at account creation using libsodium's `PublicKeyAuth.GenerateKeyPair()`. The private key (64 bytes) lives **only** inside the encrypted `user.p2e` container. It is never written anywhere else and never transmitted.

The **UID** is derived from the public key (32 bytes):
- Encode the first N bytes of the public key in a compact mixed-case Base32 alphabet (no ambiguous characters — no `0/O`, `1/I/L`).
- Prefix with `usr-`.
- Example: `usr-Xk7mN3pQ...`

UID derivation is deterministic and verifiable. Anyone with your public key can recompute your UID.

### 5.2 At-Rest Encryption (P2E containers)

All `.p2e` files use the **P2E3** format:

```
[4 bytes magic 'P2E3'][16 bytes salt][24 bytes nonce][ciphertext + 16-byte Poly1305 tag]
```

- **Key derivation:** Argon2id, 16-byte salt, output = 32-byte key.
- **Cipher:** XChaCha20-Poly1305 (`SecretAeadXChaCha20Poly1305` from libsodium).
- **AAD:** `magic + salt` are bound as additional authenticated data to prevent tag forgery.
- **Legacy format P2E2** (AES-GCM): read-only support for upgrade path. New writes always use P2E3.

Encryption is implemented in `Services/EncryptionService.cs`. Key derivation uses libsodium internals. All intermediate buffers (`salt`, `key`, `nonce`, `aad`, `cipher`) are zeroed after use via `CryptographicOperations.ZeroMemory` or `Array.Clear` where applicable.

The passphrase is a user-provided string. It is **never** stored in plaintext. If "Remember Passphrase" is enabled, the passphrase blob is DPAPI-protected (Windows) before writing to `passphrase.dpapi`.

### 5.3 Transport Encryption (ECDH + HKDF + AeadTransport)

When two peers connect:

1. **ECDH P-256 handshake.** Each side generates an ephemeral P-256 keypair and sends its DER-encoded SPKI public key to the peer.
2. **HKDF-SHA256 key derivation** (`Utilities/Hkdf.cs`). Derives four keys from the shared secret:
   - `txKey` (local → remote)
   - `rxKey` (remote → local)
   - `txBase` (16-byte nonce prefix for outbound)
   - `rxBase` (16-byte nonce prefix for inbound)
3. **AeadTransport** (`Utilities/AeadTransport.cs`) wraps the TCP stream. Every frame:
   - Header: `[0x01 type][8-byte counter BE][4-byte ciphertext length BE]`
   - Body: `XChaCha20-Poly1305(plaintext, nonce=base||counter, key=txKey, aad=type||counter)`
   - Counter is monotonically increasing. Inbound counters must strictly exceed the last seen value — out-of-order or replayed frames are rejected with `InvalidDataException`.
4. **Identity binding.** After handshake, the derived UID from the peer's ephemeral public key is checked against the expected contact UID. Mismatches trigger a security alert frame (`0xE0`, reason `0x01 = KeyMismatch`) and disconnect.

The relay server never participates in key exchange. ECDH happens directly between client sockets, over the relay-forwarded TCP stream. The relay is provably blind to plaintext.

### 5.4 Secure File Deletion

`Utilities/SecureFileWiper.cs` is drive-aware:
- **HDD**: 3-pass overwrite (random, `0xFF`, `0x00`) before `File.Delete`.
- **SSD/NVMe**: Direct `File.Delete` only. Overwriting SSDs is ineffective due to wear-leveling and wastes write cycles.

Drive type is detected via `System.Management` WMI queries and cached per-drive letter.

### 5.5 Inbound Payload Caps

All inbound data has enforced size limits before any processing:

| Payload | Cap |
|---|---|
| Chat message | 16 KB |
| Reaction emoji | 16 bytes |
| Display name | 128 bytes |
| Presence token | 8 bytes (`on`, `idle`, `dnd`, `inv`) |
| Bio | 512 bytes |
| Avatar | 256 KB |
| AeadTransport frame | 1 MB (hard) |

### 5.6 Replay Defense

For signed chat/edit/delete frames (`0xB0`, `0xB1`, `0xB2`):
- Message IDs (GUID) are tracked in a per-peer replay window (`_inboundReplayWindow`).
- TTL: 30 minutes.
- After the TTL, the entry is evicted. Within the window, a duplicate ID is dropped silently.

### 5.7 Rate Limiting (Client-Side)

`NetworkService` tracks connection attempts per remote IP in a sliding 5-minute window. Limit: **15 connection attempts per IP per 5 minutes**. Exceeded connections are refused.

---

## 6. Data Files and Storage

All user data lives under `%APPDATA%\Zer0Talk\` (the `AppDataPaths.Root` constant).

| File | Description |
|---|---|
| `user.p2e` | Account: Ed25519 keypair, display name, avatar, bio. Encrypted. |
| `contacts.p2e` | Contact list: UIDs, display names, trust flags, verification history. Encrypted. |
| `settings.p2e` | Application settings (port, theme, hotkeys, relay config, etc.). Encrypted. |
| `peers.p2e` | Peer presence and connection state. Encrypted. |
| `messages/<uid8>.p2e` | Per-contact message history (filename = first 8 chars of peer UID). Encrypted. |
| `outbox/<uid8>.p2e` | Queued outbound messages for offline delivery. Encrypted. |
| `passphrase.dpapi` | Optional DPAPI-protected remembered passphrase blob. |
| `checkpoint.json` | RegressionGuard checkpoint: last-known-good port and discovery mode. Plaintext. |
| `firewall-sync.state` | Windows Firewall rule sync state. Plaintext. |
| `Themes/*.zttheme` | User custom theme JSON files. Plaintext. |
| `Logs/app*.log` | Application logs (structured JSON or plain text). |
| `Logs/network*.log` | Network-specific logs (separated from app logs). |

**Migration:** If `%APPDATA%\P2PTalk\` exists (legacy name), `AppDataPaths.MigrateIfNeeded()` moves it to `Zer0Talk\` at startup.

**Data isolation:** The `--profile <name>` CLI flag appends a suffix to the data root (`Zer0Talk-<name>\`). Only alphanumeric, dash, and underscore characters are accepted in the suffix.

---

## 7. Startup Flow

```
Program.Main()
  ├── Parse CLI flags (--multi-instance, --profile, --safe-mode, --debug-ui, etc.)
  ├── SetCurrentProcessExplicitAppUserModelID("Zer0Talk.App")   // Taskbar identity
  ├── Acquire single-instance Mutex                             // Bring existing window to front and exit if lost
  ├── Register FirstChanceException handler for diagnostics
  ├── Build Avalonia AppBuilder and start
  └── App.OnFrameworkInitializationCompleted()
        ├── AppDataPaths.MigrateIfNeeded()
        ├── Initialize services (all AppServices singletons exist from static init)
        ├── AccountManager.HasAccount() ?
        │     NO  → Show AccountCreationWindow (modal, non-closable, full-screen gate)
        │           → GenerateKeypair → SaveAccount(user.p2e) → Surface passphrase once
        │     YES → Show UnlockWindow
        │           → User enters passphrase
        │           → LockService.Unlock(passphrase)
        │                ├── AccountManager.LoadAccount(passphrase) [throws on wrong passphrase]
        │                ├── AppServices.Passphrase = passphrase
        │                └── IdentityService.LoadFromAccount(account)
        └── Post-unlock init (settings, contacts, theme, discovery, NAT, network listening)
```

**Critical invariant:** Nothing in `Services/` accesses `AppServices.Passphrase` before unlock completes. Services that need the passphrase receive it as a parameter.

**Safe Mode** (`RuntimeFlags.SafeMode = true`): Suppresses all network and discovery activity. Used for diagnostic boot.

---

## 8. Service Layer

`AppServices` (`Services/AppServices.cs`) is a **static service locator**. All singletons are declared and initialized there. There is no DI container.

```csharp
// Key singleton declarations (subset):
AppServices.Settings       // SettingsService   – encrypted app settings
AppServices.Identity       // IdentityService   – keypair and UID
AppServices.Nat            // NatTraversalService – UPnP/STUN
AppServices.Network        // NetworkService    – TCP listener, handshake, AEAD transport
AppServices.WanDirectory   // WanDirectoryService – relay directory (REG/LOOKUP/OFFER/POLL)
AppServices.Contacts       // ContactManager    – encrypted contact list
AppServices.Peers          // PeerManager       – in-process peer state
AppServices.Events         // EventHub          – decoupled cross-service event bus
AppServices.Outbox         // OutboxService     – offline message queue
AppServices.Discovery      // DiscoveryService  – LAN/relay invite polling
AppServices.Retention      // RetentionService  – orphan file cleanup, log pruning
AppServices.ThemeEngine    // ThemeEngine       – theme management
AppServices.Notifications  // NotificationService – toast + notification center
AppServices.AutoUpdate     // AutoUpdateService – GitHub release feed check
AppServices.IpBlocking     // IpBlockingService – per-IP and CIDR blocking
AppServices.Localization   // LocalizationService – i18n string lookup
AppServices.Guard          // RegressionGuard   – anomaly detection + revert
```

### EventHub (`Services/EventHub.cs`)

The central event bus. UI windows subscribe to EventHub events instead of subscribing directly to services. This prevents window-level service lifecycle leaks.

Key events:
- `NatChanged` – NAT state changed
- `NetworkListeningChanged(isListening, port)` – TCP listener started/stopped
- `PeersChanged` – peer set updated
- `FirewallPrompt(message)` – firewall info for UI banners
- `UiPulse` – periodic UI refresh tick (see `AppServices.UiPulseKey`)
- `RegressionDetected(message)` – anomaly detected by RegressionGuard
- `MessageEdited(peerUid, messageId, newContent)` – edit received
- `MessageDeleted(peerUid, messageId)` – delete received
- `OpenConversationRequested(uid)` – notification click-to-open
- `AllMessagesPurged(summary)` – retention purge event

**Rule:** All event raises are wrapped in `try { ... } catch { }`. Handlers must not throw.

### AppServices.Events — Presence Pipeline

The presence pipeline is the primary driver of auto-connection attempts. It has internal debounce and concurrency controls:

- `PresenceEventDebounce`: 2 seconds (suppress duplicate triggers)
- `PresenceConnectDebounce`: 5 seconds per UID (prevent hammering a specific peer)
- `PresenceConnectLimiter`: `SemaphoreSlim(3,3)` — max 3 concurrent connect attempts
- `OutboxDrainLimiter`: `SemaphoreSlim(2,2)` — max 2 concurrent outbox drains

---

## 9. Connectivity Model

Connections are attempted in a deterministic priority order. Each tier is tried and only falls through if it fails.

### Tier 1 — Direct TCP

The app listens on the configured TCP port (default 26264). If a peer's address is known (via LAN discovery, WAN directory lookup, or manual entry), a direct TCP connection is attempted.

### Tier 2 — NAT Traversal

`NatTraversalService` attempts UPnP/PCP port mapping through the router. If a mapping is obtained, the external IP:port is registered with the WAN directory (relay server LOOKUP/REG protocol). The mapping is periodically verified with a hairpin test.

UPnP state machine (`MappingState`): `Idle → Discovering → GatewayDiscovered → Mapping → Mapped → Verified` (or failure paths `Failed`, `NoGatway`, `HairpinFailed`).

### Tier 3 — Relay Fallback

If direct and NAT both fail, the relay is used. The protocol is:

1. Client A sends `OFFER <sourceUid> <sessionKey>` to the relay directory.
2. Client B polls with `POLL <targetUid>` or `WAITPOLL <targetUid>` (long-poll variant).
3. On match, both clients receive the sessionKey and connect to the relay with `RELAY <sessionKey>`.
4. Relay responds `QUEUED\n` to the first connector, `PAIRED\n` to both when the second arrives.
5. **ECDH handshake begins only after receiving `PAIRED`.** This is non-negotiable — starting before pairing is confirmed is a protocol violation.
6. The relay forwards bytes bidirectionally with no interpretation.

The relay falls back to port 8443 if no explicit relay port is configured.

### Connection Mode Tracking

`NetworkService._sessionModes` tracks whether each active session is `Direct` or `Relay`. This is surfaced in the UI via contact card connection mode icons.

### EWMA Timing

`NetworkService` tracks adaptive wait times using Exponential Weighted Moving Average (EWMA):
- `_directSessionWaitMsEwma` (default 6 s)
- `_relayAckWaitMsEwma` (default 5 s)
- `_relayPairWaitMsEwma` (default 45 s)

These adapt based on observed relay/direct latency to avoid premature timeouts on slow networks.

---

## 10. Network Protocol

### AeadTransport Frame Format

```
Byte  0:       Frame type (0x01 = data, 0xE0 = security alert)
Bytes 1–8:     Counter (UInt64, big-endian)
Bytes 9–12:    Ciphertext length (UInt32, big-endian)
Bytes 13+:     Ciphertext (XChaCha20-Poly1305 authenticated)
```

The nonce for each frame is `txBase (16 bytes) || counter (8 bytes)` = 24 bytes total (XChaCha20 nonce length).

### Application Frame Types (inside AeadTransport)

| Byte | Name | Direction | Description |
|---|---|---|---|
| `0xB0` | Chat | Both | Chat message payload |
| `0xB1` | Edit | Both | Edit existing message by GUID |
| `0xB2` | Delete | Both | Delete message by GUID |
| `0xB3` | Presence | Both | Presence status token |
| `0xB4` | Avatar | Both | Avatar image blob |
| `0xB5` | Delivered ACK | Both | Delivery receipt for a message GUID |
| `0xB6` | DisplayName | Both | Display name update |
| `0xB7` | Bio | Both | Bio update |
| `0xB8` | Reaction | Both | Add/remove emoji reaction on a message |
| `0xB9` | Reply | Both | Reply metadata for a message |
| `0xBA` | Pin/Star | Both | Pin or star a message |
| `0xBC` | Version | Both | App version exchange |
| `0xE0` | Security Alert | Both | Alert frame (key mismatch, etc.) |
| (empty) | Keepalive | Both | Zero-byte frame sent every 30 s to detect dead TCP |

### WAN Directory Protocol (via relay server)

Plaintext line-based ASCII over TCP. Commands send `\n`-terminated lines; the server replies with `\n`-terminated lines.

| Command | Description |
|---|---|
| `REG <uid> <port> <pubKeyHex> <authToken>` | Register presence with WAN directory |
| `LOOKUP <uid>` | Look up peer address by UID |
| `OFFER <srcUid> <sessionKey>` | Signal intent to connect to a peer |
| `POLL <targetUid>` | Poll for pending invites (short-poll) |
| `WAITPOLL <targetUid>` | Poll with long-poll (server holds connection until invite or timeout) |
| `ACK <inviteId>` | Acknowledge a received invite |
| `RELAY <sessionKey>` | Request relay session pairing |
| `UNREG <uid>` | Remove self from directory |

Relay responses: `OK`, `ERR <reason>`, `QUEUED`, `PAIRED`, `INVITE <inviteId> <srcUid> <sessionKey>`, `INVITES [...]`, `NOT-FOUND`, `RATE-LIMITED <retryAfterMs>`.

---

## 11. Relay Server

`Zer0Talk.RelayServer` is a separate, independently deployable WinExe. It is optional. Users can run their own relay or use a community-provided one.

### Configuration (`relay-config.json`)

```json
{
  "Port": 443,
  "DiscoveryPort": 38384,
  "MaxPending": 256,
  "MaxSessions": 512,
  "PendingTimeoutSeconds": 60,
  "BufferSize": 16384,
  "MaxConnectionsPerMinute": 120,
  "BanSeconds": 120,
  "EnableFederation": false,
  "FederationPort": 8443,
  "FederationTrustMode": "AllowList",
  "PeerRelays": [],
  "ExposeSensitiveClientData": false
}
```

### Session Lifecycle

```
Client A arrives with RELAY <key>
  → RelaySessionManager.TryPairOrQueue()
  → if no peer: RelayPending created, "QUEUED\n" sent to Client A

Client B arrives with RELAY <key>
  → RelaySessionManager.TryPairOrQueue()
  → Pending found: RelaySession created, "PAIRED\n" sent to BOTH A and B
  → RelayForwarder.StartForwardingAsync() — bidirectional byte copy
```

Dead session detection: `RelaySession.IsConnected` checks `TcpClient.Connected` on both sides. Disconnected sessions are replaced immediately, not after a stale timeout.

Session outcomes (`PairOutcome`): `Paired`, `Queued`, `RejectedAlreadyQueued`, `RejectedCapacity`, `RejectedAlreadyActive`, `RejectedIncompatible`, `RejectedCooldown`.

### Rate Limiting

`RelayRateLimiter` tracks two stores: anonymous (by IP) and authenticated (by UID/token key). Anonymous limit: `MaxConnectionsPerMinute`. Authenticated limit: 6× the anonymous limit. Exceeded requests receive a `RATE-LIMITED <ms>` response. Bans are time-bounded (`BanSeconds`).

### LAN Discovery

UDP multicast on group `239.255.42.42`, port `38384`. The relay advertises itself on the local network. Clients listen for these announcements to discover relay endpoints without manual configuration.

### Federation

Optional server-to-server coordination (`EnableFederation = true`). Peer relays are listed in `PeerRelays` as `"host:port"` strings. Trust modes:
- `AllowList`: only listed peers are accepted.
- `OpenNetwork`: any peer relay can connect.

Federation provides: directory synchronization (2-minute TTL), cross-relay session routing, peer health checks (30-second interval). Federation does **not** break end-to-end encryption — it only forwards opaque bytes and directory entries.

---

## 12. UI Architecture

### Framework

Avalonia 11.3.7 with `FluentTheme`. Compiled bindings are enabled by default (`AvaloniaUseCompiledBindingsByDefault=true` in the client `.csproj`). The relay server disables compiled bindings for flexibility.

### Pattern

Strict MVVM:
- **Views** (`.axaml` + `.axaml.cs`): Only UI wiring. No business logic. Subscribes to EventHub or ViewModel events.
- **ViewModels** (`.cs`): Implements `INotifyPropertyChanged`. Contains presentation logic. No direct network or IO calls — delegates to services.
- **Services**: All logic. No Avalonia dependencies. (Exception: `AppServices.Events.RaiseUiPulse` may be called from a background service, but it only dispatches a signal — no UI manipulation from services.)

### Window Inventory

| Window | Purpose |
|---|---|
| `LoadingWindow` | Shown during async init. Prevents blank screen on slow start. |
| `UnlockWindow` | Passphrase gate. Non-closable until unlock succeeds. |
| `AccountCreationWindow` | First-run account setup. Non-closable. |
| `MainWindow` | Primary chat, contact list, status bar, notification center. |
| `SettingsWindow` | All settings including theme, hotkeys, relay, retention. |
| `NetworkWindow` | NAT status, discovered peers, WAN directory status. |
| `MonitoringWindow` | Connection health score, presence pipeline diagnostics, relay health. |
| `LogViewerWindow` | Structured live log viewer with category filtering. |
| `AddContactWindow` | Add new contact by UID. |
| `DiscoveredPeersWindow` | Peers visible on the local network. |
| `ThemeEditorWindow` | Custom theme creation and editing. |
| `ThemeSearchDialog` | Browse and import themes. |
| `LostPassphraseDialog` | Passphrase recovery flow. |

### Custom Controls

| Control | File | Purpose |
|---|---|---|
| `AvatarImage` | `Controls/AvatarImage.cs` | Cached avatar rendering |
| `FlagImage` | `Controls/FlagImage.cs` | Country flag bitmap from embedded assets |
| `EncryptedBadge` | `Controls/EncryptedBadge.axaml` | Padlock icon with tooltip |
| `VerifiedBadge` | `Controls/VerifiedBadge.axaml` | Shield icon for verified contacts |
| `PresenceDndIcon` | `Controls/PresenceDndIcon.axaml` | Do-Not-Disturb indicator |
| `PresenceIdleIcon` | `Controls/PresenceIdleIcon.axaml` | Idle indicator |
| `MarkdownToolbar` | `Controls/MarkdownToolbar.axaml` | Floating and inline formatting bar |

### Theme System

`ThemeEngine` operates in three phases:

- **Phase 1 (current):** Legacy wrapper — all calls route to `ThemeService`. Built-in themes: Dark, Light, Sandy, Butter.
- **Phase 2 (planned):** Hybrid — new `.zttheme` definitions coexist with legacy themes.
- **Phase 3 (planned):** Full engine with dynamic palettes and user-authored themes.

Custom `.zttheme` files are stored in `%APPDATA%\Zer0Talk\Themes\`. The theme save pipeline auto-backfills required compatibility color keys (`App.ItemHover`, `App.ItemSelected`, `App.AccentLight`, `App.Border`, etc.) to prevent Fluent default blue bleed.

### Localization

`LocalizationService` loads JSON files from `Resources/Localization/<lang>.json`. Keys use dot-notation paths (`"Settings.Language"`, `"MainWindow.JumpToLatest"`). Fallback: English. Language change raises `LanguageChanged` event so bound strings update live.

---

## 13. Message System

### Message Model

`Models/Message.cs` implements `INotifyPropertyChanged`. Key fields:

| Field | Type | Description |
|---|---|---|
| `Id` | `Guid` | Unique message identifier |
| `SenderUID` | `string` | Sender UID (full, with `usr-` prefix) |
| `RecipientUID` | `string` | Recipient UID |
| `Content` | `string` | Raw markdown content |
| `RenderedContent` | `string` | Post-processed markdown (code block annotation, etc.) |
| `Timestamp` | `DateTime` | Sender-local time |
| `ReceivedUtc` | `DateTime?` | UTC time of receipt on local machine |
| `DeliveryStatus` | `MessageDeliveryStatus` | `Pending`, `Sent`, `Delivered` |

`Content` setter automatically populates `RenderedContent` via `MarkdownCodeBlockLanguageAnnotator.Annotate()`.

### Delivery States

| Status | Icon | Trigger |
|---|---|---|
| `Pending` | Clock | Message enqueued locally |
| `Sent` | Single checkmark | Message written to AeadTransport without error |
| `Delivered` | Filled checkmark | Peer sent `0xB5` ACK frame with this message's GUID |

Delivery status is persisted to the message file (`messages/<uid8>.p2e`) and restored on conversation load.

### Outbox (Offline Queue)

`OutboxService` + `OutboxContainer` maintain a per-peer queue of undelivered messages in `outbox/<uid8>.p2e`. On peer connect, the outbox is drained in creation order. Operations in the queue: `Chat`, `Edit`.

Outbox drain is subject to `OutboxDrainLimiter (2,2)` and `OutboxDrainDebounce (3 s)`.

### Message Features

| Feature | Status | Notes |
|---|---|---|
| Markdown rendering | Live | Markdig via custom inline renderer |
| Spoilers | Live | `||text||` syntax, click-to-reveal |
| Code blocks | Live | Language detection + syntax annotation |
| Reply | Live (v1) | Lightweight metadata, no nested threads |
| Edit | Live | Content update + network sync (`0xB1`) |
| Delete | Live | Network sync (`0xB2`) |
| Pin / Star | Live | Per-message flags, persisted |
| Reactions | Live | Unicode emoji catalog, skin tones, flag images |
| Emoji-only sizing | Live | 72pt for pure emoji messages |
| Conversation search | Live (v1) | In-thread search, next/prev navigation |
| Message space filters | Live | All, Unread, Pinned, Starred |
| Delivery ACK | Live | `0xB5` frame → Delivered status |
| File/image transfer | **Rejected** | See [Section 2](#2-what-zer0talk-is-not) |

---

## 14. Version and Build System

### Versioning

The single source of truth is `Directory.Build.props`:

```xml
<Zer0TalkVersion>0.0.4.05</Zer0TalkVersion>
<Zer0TalkPrereleaseTag>Alpha</Zer0TalkPrereleaseTag>
```

Both `Zer0Talk.csproj` and `Zer0Talk.RelayServer.csproj` inherit these values. A MSBuild target (`ValidateSharedVersionSync`) enforces at build time that `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion` all match the shared values. A version drift between the two projects is a **build error**.

**InformationalVersion format:** `0.0.4.05-Alpha`

`AppInfo.Version` (and `RelayAppInfo.Version`) parse the assembly's `AssemblyInformationalVersionAttribute`, stripping the prerelease tag and any `+<commit>` suffix automatically.

### Build Configurations

| Config | Analyzers | AppHost | Notes |
|---|---|---|---|
| Debug | Off | Enabled | Avalonia Diagnostics included |
| Release | Recommended | Enabled | Analyzers on, optimized |

**Always build both configurations before declaring work done.** This is a standing requirement — never ship Debug-only validation.

#### Build Commands

```powershell
dotnet build .\Zer0Talk.sln -c Debug
dotnet build .\Zer0Talk.sln -c Release
```

Or use the `dotnet: build debug+release clean-lock` task defined in `.vscode/tasks.json`.

### ConcurrentBuild

`<ConcurrentBuild>false</ConcurrentBuild>` is set explicitly to prevent DLL locking races during rapid iterative builds on the same machine.

### AssemblyInfo

`GenerateAssemblyInfo=true` and `GenerateTargetFrameworkAttribute=false`. Assembly attributes are auto-generated from csproj values. Do not maintain a manual `AssemblyInfo.cs`.

---

## 15. Testing

### Test Project

`Tests/Zer0Talk.Tests.csproj` — xUnit 2.9, targeting .NET 9. References the main `Zer0Talk.csproj` directly.

### Coverage Areas

| Test File | What It Covers |
|---|---|
| `UidNormalizationTests.cs` | Prefix trimming, case normalization, equality |
| `OutboxServiceTests.cs` | Enqueue, edit dedupe, queued-content update, cancel removal |
| `MessageModelTests.cs` | Message property change notifications, content normalization |
| `MessageReactionTests.cs` | Reaction aggregate counting, add/remove logic |
| `MessageSpaceFilterTests.cs` | Pin/Star/Unread filter logic |
| `SpoilerTokenizerTests.cs` | `\|\|...\|\|` tokenization, edge cases |
| `TrustCeremonyFormatterTests.cs` | Fingerprint display formatting |
| `BackupArchiveFormatTests.cs` | `.ztbackup` archive structure |
| `ConnectionHealthScoringTests.cs` | Score calculation from diagnostic counters |
| `IpCountryDetectionTest.cs` | IP → country code resolution |
| `Controls/` | Control-level unit tests |

### Federation Tests

Defined as VS Code tasks and PowerShell scripts in `scripts/`:

| Script | Purpose |
|---|---|
| `federation_smoke_check.ps1` | Basic federation connectivity and command round-trip |
| `federation_reliability_check.ps1` | Sustained operation under repeated connect/disconnect |
| `federation_soak_check.ps1 -DurationMinutes 30` | 30-minute sustained relay usage |
| `federation_soak_check.ps1 -DurationMinutes 10 -Strict` | 10-minute strict mode |

---

## 16. CI/CD

### Quality Gate (`.github/workflows/quality-gate.yml`)

Runs on every push and pull request to `main`:

1. `dotnet build` — Debug configuration
2. `dotnet build` — Release configuration
3. `dotnet test` — xUnit test suite
4. Federation smoke test (`scripts/federation_smoke_check.ps1`)

Artifacts uploaded: `.trx` test results, federation smoke log.

### Bare `catch { }` Guard

The quality gate runs a static check that blocks PRs that **introduce new bare `catch { }` blocks** in `App/`, `Services/`, or `ViewModels/`. Existing bare catches in utility/infrastructure code are grandfathered under `GlobalSuppressions.cs`.

### Release Automation

Automated workflow for building, packaging, manifest generation, and upload to `AnotherLaughingMan/Zer0Talk-Releases`:
- Builds both Debug and Release.
- Produces installer + zip archive.
- Generates `update-manifest.json` with `version`, `tag`, `installerUrl`, `sha256`, and release notes URL.
- Authenticode signs binaries (`scripts/sign-binaries.ps1`).

**Update manifest is consumed by `AutoUpdateService`** at runtime. Update sources are restricted to `github.com`, `api.github.com`, `objects.githubusercontent.com`, `githubusercontent.com`, and `github-releases.githubusercontent.com`. Downloads outside these hosts are rejected. Installer is Authenticode-verified before execution.

---

## 17. Coding Standards

These are requirements, not suggestions.

### File Size

Files over **600 lines are too long.** Split into focused partial classes (the codebase already does this extensively — e.g., `NetworkService.cs` + sub-files, `ThemeEngine.cs` + `.Loading.cs` + `.Resources.cs`). Allow slack up to 600 lines. Do not let files grow unbounded.

### Naming

- Do not use names that are reserved by the framework or BCL (e.g., `System`, `Microsoft`, `Application`).
- Partial class files follow the convention `ServiceName.SubResonsibility.cs`.
- All `async` methods are suffixed `Async`.

### Nullable

`Nullable=enable` is enforced. Every parameter and return type is nullable-annotated. Do not use `!` suppression unless the nullability is guaranteed by prior logic and cannot be expressed in the type system.

### Error Handling

- **Never use bare `catch { }`** in `Services/`, `ViewModels/`, or application logic. At minimum, log the exception.
- All `EventHub.Raise*()` methods wrap in `try { ... } catch { }` — this is the one permitted exception to the rule, as event handler failure must never crash callers.
- Use `CancellationToken` on every async operation that could block.
- Set explicit timeouts; no infinite waits.
- Fail fast on invalid input at system boundaries (user input, inbound network data). Trust internal code.

### Security in Code

- Zero sensitive byte arrays after use: `CryptographicOperations.ZeroMemory` or `Array.Clear`.
- Never log plaintext message content, passphrases, or private key material.
- All inbound sizes must be bounded before allocation or processing (see [Section 5.5](#55-inbound-payload-caps)).
- No SSRF: all outbound HTTP requests from `LinkPreviewService` and `AutoUpdateService` validate the target against an explicit allowlist. Private/loopback/link-local/ULA addresses are rejected.
- No arbitrary URI launching: `UrlLauncher` rejects non-`http`/`https` schemes.

### Threading

- UI updates must happen on the Avalonia UI thread. Use `Dispatcher.UIThread.Post()` or `InvokeAsync()` when transitioning from background tasks.
- All cross-thread shared state uses `ConcurrentDictionary`, `Interlocked`, or `lock`.
- `SemaphoreSlim` is used to bound concurrency on network operations (never unbounded `Task.Run` floods).

### Logging

- Use `Logger.Log(message, LogLevel, source, category)` for all logging.
- Use `categoryOverride: "network"` for network-path logs to keep them out of the app log.
- Use `categoryOverride: "app"` for application-path logs.
- `Logger.Warning` and `Logger.Error` should include exception messages.
- Never log message content, passphrases, or key material.

### Serialization

- All JSON serialization uses `SerializationDefaults.Indented` (for persisted files) or the default `JsonSerializerOptions`.
- Use `System.Text.Json` only. No Newtonsoft.

---

## 18. Security Requirements

Security is not a layer — it is the foundation. Every change must be evaluated for:

| Threat | Mitigation in place |
|---|---|
| Eavesdropping | AeadTransport (XChaCha20-Poly1305), relay sees ciphertext only |
| Man-in-the-middle | ECDH → identity binding → UID mismatch → security alert + disconnect |
| Replay attacks | Monotonic AeadTransport counter; message-ID dedup window (30 min) |
| Data theft at rest | All .p2e files are P2E3-encrypted with Argon2id + XChaCha20-Poly1305 |
| Passphrase exposure | DPAPI protection; buffers zeroed after use |
| Injection (UI) | Avalonia data binding with compiled bindings; no eval of user content as code |
| SSRF | Allowlist for all outbound HTTP; private IP ranges rejected |
| Protocol handler abuse | UrlLauncher enforces http/https scheme only |
| DoS (connection flood) | 15 connections/5 min per IP client-side; relay rate limiter per IP + UID |
| Sensitive log exposure | No plaintext content in logs; metadata-only entries |
| Update chain attack | Trusted HTTPS hosts allowlist + Authenticode verify before exec |
| Storage tampering | Corrupt/unreadable conversation files are quarantined, not fail-opened |

Any security regression — even minor — must be documented in `CHANGELOG.md` under `### Fixed` with explicit `Security audit remediation:` prefix.

---

## 19. Decisions and Policies

These are binding decisions made by the project owner. Proposals to change them require an explicit discussion.

### Rejected Features (Policy)

- **File/image transfer:** Rejected due to CSAM/exploitation risk and the reputational liability of being a file-sharing vector. No attachment system will be built.
- **AI-assist features:** Deferred. The privacy trust model for AI interaction has not been defined. No AI features will be added until a sound model exists.

### Protocol Invariants

These must never be violated:

1. **ECDH handshake starts only after relay sends `PAIRED`.** Starting before is a bug that breaks the security model.
2. **Relay operators never see plaintext.** Session keys derived inside the ECDH are end-to-end between clients.
3. **UID is derived from public key.** It cannot be chosen arbitrarily. Changing the derivation algorithm breaks compatibility.

### Version Compatibility

`AppInfo.IsVersionCompatible()` currently requires an exact version match. Looser compatibility rules must be agreed before relaxing this. Version mismatches are logged and surfaced to users via the monitoring window.

### Single Binary Per Role

The client and relay server are separate binaries. They share `Directory.Build.props` for version alignment but have no shared runtime code. This separation is intentional: relay operators should not need the full client to run a relay.

### AMD CPU CCD Affinity — Why V-Cache Is Preferred

Zer0Talk exposes a **CCD Affinity** setting for AMD multi-CCD processors (Ryzen 7000X3D / 9000X3D series). The default is **Auto**, which detects the 3D V-Cache CCD at runtime and pins the process to it.

**Why the V-Cache die is the better choice for Zer0Talk:**

Zer0Talk's hot paths are dominated by work that is sensitive to L3 cache availability, not raw clock speed:

- **XChaCha20-Poly1305** encryption/decryption — cipher state fits in L3; cache misses are the bottleneck
- **X25519/Ed25519** key operations — elliptic-curve arithmetic benefits from keeping the working set resident
- **Message indexing and contact lookups** — repeated reads of in-memory structures that reward a large L3
- **UI rendering pipeline** — layout and composition passes reuse the same data structures repeatedly

The 3D V-Cache die carries approximately **96 MB of L3 per CCD** versus ~32 MB on the non-V-Cache die. The non-V-Cache die has a modest clock advantage (~200–400 MHz higher boost), which helps single-thread latency but provides negligible gain when the workload is already L3-bound.

**Design rule:** The `Auto` mode in `ApplyCcdAffinityImmediate()` intentionally selects the V-Cache CCD when detected. Do not change this default. If a future workload profile shifts toward single-thread latency rather than cache throughput, revisit with profiling data before altering the preference.

---

## 20. Changelog Discipline

`CHANGELOG.md` is maintained at the project root. It follows Keep a Changelog conventions with three heading types: `### Added`, `### Updated`, `### Fixed`.

**Rules:**

1. Every unit of shipped work gets a `CHANGELOG.md` entry under `## [Unreleased]` before the PR merges — not after.
2. On release day: copy `[Unreleased]` into a new `[version-Alpha] - YYYY-MM-DD` section and reset `[Unreleased]` to empty placeholders.
3. Security regressions fixed must use the prefix `Security audit remediation:` in the `### Fixed` entry.
4. Relay reliability fixes use the prefix `Relay reliability remediation:`.
5. Do not rewrite history. Past entries are immutable once released.

---

*End of Developer Bible — keep this document current as the product evolves.*
