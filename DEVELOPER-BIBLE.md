# Zer0Talk — Developer Bible

**Version:** 0.0.4.05-Alpha  
**Last Audited:** 2026-03-10  
**Status:** Living document. Treat with the same seriousness as production code.

> **On AI tooling:** This project uses AI agents as part of its development workflow. This is not "vibe coding." Every decision — architectural, cryptographic, product, and policy — is made by the project owner. The AI is a tool, directed deliberately, the same way any other tool is. See [Section 21](#21-ai-in-the-development-workflow) for full disclosure.

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
21. [AI in the Development Workflow](#21-ai-in-the-development-workflow)

---

## 1. What Zer0Talk Is

Zer0Talk is a **private, end-to-end encrypted peer-to-peer messaging application** for Windows. There is no central server that stores, routes, or mediates your messages. Every message is encrypted before it leaves the sender's machine and decrypted only on the recipient's machine. No third party — including relay operators — can read the content of conversations.

**Core properties:**

- **Keypair-based identity.** Each account is an Ed25519 keypair. Your UID is deterministically derived from your public key. There are no usernames or email addresses registered with any central authority.
- **Blind relay, zero knowledge.** Optional relay infrastructure can be self-hosted. Relays are not servers in the traditional sense — they retain no data and serve no content. A relay only facilitates the initial TCP connection request and forwards an opaque encrypted byte stream. The ECDH handshake and session keys are established end-to-end, through the relay-forwarded stream. The relay is provably blind to all plaintext.
- **All data encrypted at rest.** Every persistent file is encrypted using XChaCha20-Poly1305 with Argon2id key derivation. Nothing is stored in plaintext.
- **Self-sovereign.** Users control their data directory, their keys, their relay, and their contacts. Account deletion is performed via **Settings → Danger Zone → Delete Account**, which performs a secure multi-pass wipe of all identity and data files.
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

UPnP state machine (`MappingState`): `Idle → Discovering → GatewayDiscovered → Mapping → Mapped → Verified` (or failure paths `Failed`, `NoGateway`, `HairpinFailed`).

### Tier 3 — Relay Fallback

If direct and NAT both fail, the relay is used. The full end-to-end protocol is:

1. Client A calls `TryOfferRendezvousAsync()`, which sends `OFFER <sourceUid> <sessionKey>` to the relay directory. The call retries **up to 3 times** (2-second delay between each attempt). If all retries fail the relay connection is abandoned.
2. Client B receives the invite via `POLL <targetUid>` or `WAITPOLL <targetUid>` (long-poll variant).
3. Both clients independently connect to the relay and send `RELAY <sessionKey>`.
4. The relay responds `QUEUED\n` to whichever client arrives first. **That client blocks and waits for `PAIRED\n` — it does not begin ECDH yet.**
5. When the second client arrives, the relay sends `PAIRED\n` to **both** clients simultaneously.
6. **ECDH handshake begins only after receiving `PAIRED`.** This is non-negotiable — starting before pairing is confirmed is a protocol invariant violation.
7. The relay forwards bytes bidirectionally with no interpretation. The relay is blind to all plaintext.

Error responses from the relay (`ERR <reason>\n`) cause structured log entries. Specific handling:
- `ERR cooldown` / `ERR rate-limit` → wait 3.1 s then allow retry
- `ERR blocked` / `ERR capacity` / `ERR already-active` / `ERR already-queued` → log and abort

The relay falls back to port 8443 if no explicit relay port is configured.

### OFFER Retry Logic

`WanDirectoryService.TryOfferRendezvousAsync()` retries an OFFER up to **3 times** with a **2-second delay** between attempts. Each attempt logs `"OFFER delivered to {endpoint} on attempt X/3"` or `"OFFER attempt X/3 failed: {exception}"`. The first successful attempt returns immediately; if all three fail, false is returned and relay fallback is abandoned.

### Connection Mode Tracking

`NetworkService._sessionModes` tracks whether each active session is `Direct`, `NAT`, or `Relay`. This is surfaced in the UI via contact card connection mode icons and summarised in the monitoring window connection-stats view.

### Relay Candidate Selection — Happy Eyeballs

When connecting to a relay, `NetworkService.ConnectToFirstRelayAsync()` fires all candidates in parallel with a **250 ms stagger** between each attempt (applied in candidate order). The first TCP connection that completes wins; all others are cancelled. This mirrors the Happy Eyeballs algorithm (RFC 8305) applied to relay endpoints instead of IPv4/IPv6 races.

Candidates are pre-sorted by:
1. Health score descending (`GetRelayHealthScore` — EWMA of success rate / latency)
2. Distance from the preferred relay ascending

This means the healthiest, closest relay gets the first attempt, while slower candidates start with a small delay rather than being tried sequentially.

### EWMA Adaptive Timeouts

`NetworkService` tracks adaptive wait times using Exponential Weighted Moving Average (EWMA, α = 0.25). Each value adapts after every observed connection so timeouts shrink on fast networks and expand on slow ones:

| Field | Default | What it gates |
|---|---|---|
| `_directSessionWaitMsEwma` | 6 000 ms | Time to see an inbound direct session appear in `_sessions` after a connect attempt |
| `_relayAckWaitMsEwma` | 5 000 ms | Time from TCP connect to the relay's `QUEUED` or `PAIRED` acknowledgment |
| `_relayPairWaitMsEwma` | 20 000 ms | Time from receiving `QUEUED` until `PAIRED` arrives (peer arrival latency) |

Updated via `ObserveRelayAckWait()` and `ObserveRelayPairWait()` called on every successful or timed-out relay connection.

### Keepalive Frames

After a session is established — for **both relay and direct connections** — a background task sends an **empty (zero-byte) frame every 30 seconds**. If the write fails, the session `CancellationToken` is cancelled immediately, tearing down the session. This prevents ghost sessions (TCP connections that appear open but are silently dead due to NAT expiration or network interruption) from accumulating in the active session table.

The 30-second interval is intentionally shorter than typical NAT TCP mapping lifetimes (60–300 s), ensuring dead paths are detected well before the mapping expires.

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
| (empty) | Keepalive | Both | Zero-byte frame sent every 30 s on both relay and direct sessions. If the write fails, the session CancellationToken is cancelled immediately, tearing down the session. Prevents ghost sessions from accumulating after silent TCP drops. |

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
| `RELAY-PEERS` | Request list of known peer relay addresses (gossip discovery) |

Relay responses: `OK`, `ERR <reason>`, `QUEUED`, `PAIRED`, `INVITE <inviteId> <srcUid> <sessionKey>`, `INVITES [...]`, `NOT-FOUND`, `RATE-LIMITED <retryAfterMs>`.

---

## 11. Relay Server / Hosted Server

`Zer0Talk.RelayServer` is a separate, independently deployable WinExe. It is optional. Users can run their own instance or connect to a community-provided one.

The binary now serves **three distinct roles**, each behind its own feature flag:

| Role | Feature Flag | Default Port | Purpose |
|---|---|---|---|
| **P2P Relay** | (always on) | 443 | Client-to-client session pairing and WAN directory (REG/LOOKUP/OFFER/POLL) |
| **Relay Federation** | `EnableFederation` | 8443 | Relay-to-relay coordination (cross-relay session bridging, federated OFFER forwarding) |
| **Hosted Server** | `EnableHosting` | 8444 | Persistent accounts, room lifecycle, encrypted message queuing |
| **S2S Room Federation** | `EnableHosting` + `PeerHostedServers` | 8445 | Server-to-server room event routing between peer hosted servers |

When all three modes are on, four TCP listeners run simultaneously. End-to-end encryption is preserved across all modes — the server never sees plaintext.

### Configuration (`relay-config.json`)

`RelayConfigStore` loads from `%APPDATA%\Zer0TalkRelay\relay-config.json`. Missing fields are auto-defaulted and written back on startup.

```json
{
  // Core relay
  "Port": 443,
  "DiscoveryPort": 38384,
  "AutoStart": true,
  "DiscoveryEnabled": true,
  "RelayAddressToken": "",
  "MaxPending": 256,
  "MaxSessions": 512,
  "PendingTimeoutSeconds": 60,
  "BufferSize": 16384,
  "MaxConnectionsPerMinute": 120,
  "BanSeconds": 120,
  "ExposeSensitiveClientData": false,
  "OperatorBlockSeconds": 1800,

  // UI behavior
  "ShowInSystemTray": true,
  "MinimizeToTray": true,
  "StartMinimized": false,
  "RunOnStartup": false,
  "EnableSmoothScrolling": true,

  // Relay federation (server-to-server relay coordination)
  "EnableFederation": false,
  "FederationPort": 8443,
  "FederationTrustMode": "AllowList",
  "PeerRelays": [],
  "MaxFederationPeers": 10,
  "FederationSyncIntervalSeconds": 30,
  "FederationSharedSecret": "",

  // Hosted server (persistent accounts + rooms)
  "EnableHosting": false,
  "HostingPort": 8444,
  "HostingS2SPort": 8445,
  "HostingAddress": "",
  "PeerHostedServers": [],
  "MaxRegisteredUsers": 10000,
  "MaxRoomsPerUser": 10,
  "MaxMembersPerRoom": 12,
  "RoomMessageQueueDepth": 200,
  "DataDirectory": "relay-data"
}
```

`DataDirectory` is relative to `%APPDATA%\Zer0TalkRelay\`. The SQLite database is written to `<DataDirectory>/server.db`.

### Session Lifecycle

```
Client A arrives with RELAY <key>
  → RelaySessionManager.TryPairOrQueue()
  → if no peer: RelayPending created, "QUEUED\n" sent to Client A
  → Client A blocks waiting for "PAIRED\n" (up to _relayPairWaitMsEwma, default 20 s)

Client B arrives with RELAY <key>
  → RelaySessionManager.TryPairOrQueue()
  → Pending found: RelaySession created
  → "PAIRED\n" sent to BOTH A and B simultaneously
  → Both clients unblock and begin ECDH handshake
  → RelayForwarder.StartForwardingAsync() — bidirectional byte copy
```

**Protocol invariant:** `PAIRED` must be sent to both sides before the forwarder starts. The relay guarantees this ordering. Clients must not send any ECDH bytes until `PAIRED` is received.

**Dead session detection:** `RelaySession.IsConnected` checks `TcpClient.Connected` on both sides. If a new client arrives with the same session key and `IsConnected` is false, the dead session is evicted immediately — no waiting. Pending (un-paired) sessions are also evicted if their TCP is dead **or** they are older than **2 seconds**, allowing the incoming client to take their place.

Session outcomes (`PairOutcome`): `Paired`, `Queued`, `RejectedAlreadyQueued`, `RejectedCapacity`, `RejectedAlreadyActive`, `RejectedIncompatible`, `RejectedCooldown`.

### Relay Acknowledgment Protocol

The relay sends control lines over the raw TCP stream before forwarding begins:

| Message | Sent to | Meaning |
|---|---|---|
| `QUEUED\n` | First-arriving client | Registered; waiting for peer to arrive |
| `PAIRED\n` | Both clients | Peer arrived; begin ECDH handshake |
| `ERR <reason>\n` | Requesting client | Request rejected (see reasons below) |

Known `ERR` reasons: `already-active`, `already-queued`, `cooldown`, `rate-limit`, `blocked`, `capacity`.

Client-side handling:
- `cooldown` / `rate-limit` → wait 3.1 s before retry
- `blocked` / `capacity` / `already-active` / `already-queued` → log and abort (no retry)

**Cross-relay bridging (federation):** When `QUEUED` is sent and federation is enabled, a background task waits 500 ms and then queries peer relays for the session key. If a peer relay has the counterpart waiting, the local relay atomically claims its pending session (`TryClaimForBridge`) and calls `OpenBridgeAsync` on the peer to establish a raw TCP bridge. Both clients receive `PAIRED` through their respective sockets and subsequent bytes are piped bidirectionally. The relay-to-relay TCP connection stays open for the duration of the session, then closes.

### Rate Limiting

`RelayRateLimiter` tracks two stores: anonymous (by IP) and authenticated (by UID/token key). Anonymous limit: `MaxConnectionsPerMinute`. Authenticated limit: 6× the anonymous limit. Exceeded requests receive a `RATE-LIMITED <ms>` response. Bans are time-bounded (`BanSeconds`).

### LAN Discovery

UDP multicast on group `239.255.42.42`, port `38384`. The relay advertises itself on the local network. Clients listen for these announcements to discover relay endpoints without manual configuration.

### Federation

Optional server-to-server coordination (`EnableFederation = true`). Peer relays are listed in `PeerRelays` as `"host:port"` strings. Trust modes:
- `AllowList`: only listed peers are accepted.
- `OpenNetwork`: any peer relay can connect.

Federation does **not** break end-to-end encryption — it only forwards opaque bytes and directory entries.

#### Federation Protocol Commands

All federation commands are sent over TCP and require a shared secret (`FederationSharedSecret` config field, if set). Commands are line-based ASCII, same as the client directory protocol.

| Command | Description |
|---|---|
| `RELAY-HELLO <secret>` | Identity handshake — returns `OK-HELLO <token> <maxSessions> <activeSessions>` |
| `RELAY-LOOKUP <uid> <secret>` | Look up a UID in the peer's directory — returns `PEER <uid> <host> <port> <age>` or `MISS` |
| `RELAY-HEALTH <secret>` | Peer load stats — returns `HEALTH <load%> <pending> <active> <max>` |
| `RELAY-DIR-DUMP <secret>` | Bulk directory sync — returns all registered UIDs as a semicolon-delimited list |
| `RELAY-DISCONNECT <uid> <secret>` | Tell a peer to evict a UID from its local cache |
| `RELAY-SESSION-QUERY <sessionKey> <secret>` | Check whether a session key has a pending client on the peer — returns `HAS <key>` or `MISS` |
| `RELAY-BRIDGE <sessionKey> <secret>` | Claim the peer's pending session, pipe both streams together — returns `OK-BRIDGE` then raw TCP |
| `RELAY-OFFER <targetUid> <sourceUid> <sessionKey> <secret>` | Forward a rendezvous invite to a peer serving the target UID — returns `OK <inviteId>` |

#### Federation Optimizations

- **Persistent inter-relay connections (`PersistentFederationConnection`):** Each peer relay has one long-lived TCP connection used for health checks and directory sync. The connection auto-reconnects on failure. This eliminates per-command TCP setup overhead.
- **Parallel directory lookup (`ParallelLookupAsync`):** UID lookups fan out to all peers simultaneously; the first `PEER` response wins and all in-flight queries are cancelled. Replaces the prior sequential scan.
- **Auto-reconnect with throttle:** If a peer fails 3+ consecutive health checks, a reconnect is scheduled (rate-limited to once per 60 s) rather than waiting for the next health-check cycle.
- **Cross-relay session bridging:** Clients on different federated relays pair transparently. See Session Lifecycle above for the detailed flow.
- **Federated OFFER forwarding:** When a client sends an OFFER for a UID that is not locally registered, the relay performs a federated lookup and forwards the invite to the relay hosting that UID via `RELAY-OFFER`.

#### Federation health checks

Health checks run every 30 seconds (configurable via `FederationSyncIntervalSeconds`). A check sends `RELAY-HEALTH` to each peer. Three or more consecutive failures mark the peer as degraded and trigger the auto-reconnect path (rate-limited to once per 60 s).

### Hosted Server Mode (`EnableHosting = true`)

When hosting is enabled, `HostedServerHost` starts a separate listener on `HostingPort` (default 8444). This provides persistent accounts and room management on top of the P2P relay infrastructure.

#### Authentication

Every client connection to the hosted server begins with a challenge-response handshake:

```
Server → Client: CHALLENGE <hexNonce>
Client → Server: ACCOUNT-REG <uid> <pubkeyHex> <sig-of-nonce>   ← first-time registration
              OR ACCOUNT-AUTH <uid> <authToken>                  ← subsequent logins
Server → Client: OK <authToken>   (ACCOUNT-REG)
              OR OK               (ACCOUNT-AUTH)
```

`ACCOUNT-REG` verifies the Ed25519 signature over the nonce and stores the account in SQLite. `authToken` is returned to the client and cached with DPAPI encryption (`RoomKeyStore`). The nonce is recorded in `UsedNonces` to prevent replay attacks.

#### Room Protocol Commands

Post-authentication command loop on the hosted server port:

| Command | Description |
|---|---|
| `ROOM-CREATE <name> <memberCap>` | Create room; caller becomes admin. Returns `OK <roomId>` |
| `ROOM-INVITE <roomId> <targetUid> [targetHomeServer]` | Invite user; pushes `ROOM-INVITED` if online; routes via S2S if `targetHomeServer` given |
| `ROOM-JOIN <roomId>` | Accept pending invite; triggers `ROOM-AWAITING-KEY` to admin |
| `ROOM-LEAVE <roomId>` | Leave; broadcasts `ROOM-MEMBER-LEFT`; signals rekey |
| `ROOM-MEMBERS <roomId>` | Get member list with roles and home servers |
| `ROOM-KICK <roomId> <targetUid>` | Moderator+ only; broadcasts `ROOM-MEMBER-LEFT`; signals rekey |
| `ROOM-BAN <roomId> <fingerprint>` | Admin only; bans by key fingerprint |
| `ROOM-TRANSFER-ADMIN <roomId> <targetUid>` | Transfer admin role; broadcasts `ROOM-ADMIN-CHANGED` |
| `ROOM-MSG <roomId> <ciphertextHex>` | Server-route encrypted message (only used when admin is offline) |
| `PING` | Returns `PONG` |

#### Server Push Events (server → client, any time)

| Event | Trigger |
|---|---|
| `ROOM-INVITED <roomId> <inviterUid>` | Invited while connected |
| `ROOM-MEMBER-JOINED <roomId> <uid>` | Member joined |
| `ROOM-MEMBER-LEFT <roomId> <uid>` | Member left or was kicked |
| `ROOM-ADMIN-ONLINE <roomId> <relayKey>` | Admin came online (P2P relay key for direct messaging) |
| `ROOM-ADMIN-OFFLINE <roomId>` | Admin went offline (server-routing mode activates) |
| `ROOM-AWAITING-KEY <roomId> <uid>` | New member joined; admin must send group key |
| `ROOM-DELIVER <roomId> <senderUid> <ciphertextHex>` | Server-routed message fan-out |
| `ROOM-REKEY <roomId>` | Group rekey required (kick/leave event) |
| `ROOM-ADMIN-CHANGED <roomId> <newAdminUid>` | Admin transferred |

#### Hybrid Routing Model

Rooms operate in two modes determined by admin online status:

- **P2P mode (admin online):** Admin broadcasts its relay session key (`ROOM-ADMIN-ONLINE <roomId> <relayKey>`). Members connect directly via the P2P relay. The server is not in the message path.
- **Server-routing mode (admin offline):** Members send `ROOM-MSG` to the hosted server. The server fans out ciphertext to all online members as `ROOM-DELIVER` and queues up to `RoomMessageQueueDepth` (default 200) messages in an in-memory ring buffer. **Messages are never persisted to disk** — only in memory.

#### SQLite Schema

Database: `<DataDirectory>/server.db`. WAL mode, foreign keys enabled.

| Table | Purpose |
|---|---|
| `Users` | Persistent accounts (`uid`, `public_key`, `auth_token`, `registered_at`, `last_seen`) |
| `Rooms` | Room definitions (`room_id`, `display_name`, `admin_uid`, `home_server`, `member_cap`, `room_public_key`) |
| `RoomMembers` | Membership + federation home server (`room_id`, `uid`, `role`, `home_server`, `joined_at`) |
| `BannedFingerprints` | Per-room fingerprint bans (`room_id`, `fingerprint`, `banned_at`, `banned_by_uid`) |
| `UsedNonces` | Replay protection for account registration |

#### S2S Room Federation

When `PeerHostedServers` is populated, `RoomFederationManager` establishes authenticated persistent TCP connections on `HostingS2SPort` (8445).

Handshake:
```
Acceptor → Connector: S2S-CHALLENGE <hexNonce>
Connector → Acceptor: S2S-HELLO <selfId> <HMAC-SHA256(FederationSharedSecret, nonce)>
Acceptor → Connector: S2S-OK | S2S-ERR <reason>
```

Post-auth commands:
- `S2S-NOTIFY <targetUid> <message>` — deliver room event to a locally-connected user
- `S2S-ROOM-INVITE <targetUid> <roomId> <inviterUid>` — queue a cross-server room invite
- `S2S-PING` / `S2S-PONG` — keepalive

The `selfId` used in `S2S-HELLO` is `HostingAddress` (if set) or `RelayAddressToken`.

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

1. **ECDH handshake starts only after relay sends `PAIRED`.** Clients receiving `QUEUED` must block and wait for a subsequent `PAIRED`. Sending ECDH bytes before `PAIRED` desynchronises both sides — the relay has not yet connected them. This invariant is enforced in code inside `TryConnectViaRelaySessionAsync`; it is not merely a convention.
2. **Relay operators never see plaintext.** ECDH is performed over the relay-forwarded TCP stream. The relay pipes opaque bytes. Session keys are derived end-to-end between clients.
3. **UID is derived from public key.** It cannot be chosen arbitrarily. Changing the derivation algorithm breaks compatibility.
4. **Active sessions must send keepalive frames.** All relay and direct sessions run a 30-second keepalive loop. Suppressing keepalives allows ghost sessions to accumulate, blocking new connections from the same peer.

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

## 21. AI in the Development Workflow

> **This is not "vibe coding."**
>
> Vibe coding is the practice of throwing prompts at an AI and shipping whatever comes out. That is not what happens here. The project owner directly directs the AI at every step: deciding what to build, what not to build, how it should work, what the security model is, and whether any given output is acceptable. The project owner also contributes directly — code where needed, all graphics, all sounds, all product and policy decisions. The AI executes under that direction. If the output is wrong, it gets corrected. If the approach is wrong, the AI is redirected. The human is in the loop at all times.

This project uses AI coding agents as an active part of the development workflow. This includes but is not limited to GitHub Copilot (with various underlying models including Claude, GPT, and others), and any other AI agent that proves useful. This is a deliberate choice, made transparently.

**What AI agents do in this project:**

- Implement features, refactors, and bug fixes under human direction
- Read and navigate the codebase to gather context before making changes
- Write, validate, and iterate on code until builds are clean
- Update documentation (CHANGELOG, Developer Bible, audit docs) as work lands
- Run builds and interpret compiler output
- Commit and push to source control on instruction

**What the project owner contributes directly:**

- All product and policy decisions (what gets built, what gets rejected, why)
- All architectural decisions — including the entire security and cryptography model
- All visual assets: graphics, icons, UI design, avatars, flags
- All audio assets: sounds, notifications, audio integration
- Code contributions where judgment, context, or security sensitivity demands direct authorship
- Final review and approval of every change before it ships

**What AI agents do not do:**

- Make architectural decisions autonomously. The project owner directs all significant choices.
- Bypass the build gate. Every change is validated with both Debug and Release builds before committing.
- Touch cryptographic primitives without explicit review. Security-critical code is treated as highest-risk regardless of who writes the first draft.
- Push breaking changes silently. Destructive or hard-to-reverse operations require explicit confirmation.
- Decide what the product is. That is the project owner's call, always.

**On transparency:**

The use of AI tooling in this workflow is stated here openly. Contributors and users who have concerns are encouraged to review the output — the code, tests, documentation, and commit history — on its own merits. AI-assisted code is subject to the same standards as any other code in this repository: it must be correct, secure, and maintainable. Authorship of a line tells you nothing about its quality; reading it does.

**AI in the product vs. AI in the workflow:**

These are separate concerns. AI-assist *features inside Zer0Talk* remain deferred (see Section 19 — privacy trust model not yet defined). AI *as a development tool* for building Zer0Talk is already in use and will continue to be.

---

*End of Developer Bible — keep this document current as the product evolves.*
