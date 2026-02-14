# Relay Server Design Spec

## Summary
This document defines the initial design for the Zer0Talk Relay Server. The relay is a separate executable that forwards encrypted peer traffic, provides IP obfuscation by relaying connections, participates as a full node in the P2P network, and includes built-in monitoring. The relay stores no message data.

## Goals
- Provide a reliable fallback path when direct P2P + NAT traversal fails.
- Obfuscate peer IPs by routing traffic through the relay.
- Operate as a full node (peer discovery, health checks) without storing messages.
- Offer a GUI with a built-in console for live monitoring.
- Run on Windows 11 Pro/Enterprise and Windows Server versions compatible with .NET 9.

## Non-Goals
- Message decryption or inspection by the relay.
- Long-term storage of traffic or user data.
- Bypassing OS-level network inspection tools (see Security Notes).

## Constraints
- .NET 9 desktop app with a GUI and embedded console.
- Windows 11 Pro+ and Windows Server only.
- Relay is slower than direct P2P and should be used as fallback.
- No central data retention.
- Default relay port is 443 to improve reachability behind restricted NATs.
- Optional Windows Service mode for auto-start.
- Relay versioning matches the client format (major.minor.patch.build, e.g., 0.0.2.09).
- UI must show the same orange tag used by the client ("In-Dev Alpha") near the version at the top of the window.

## High-Level Architecture
- Relay Server process accepts inbound TCP connections.
- A session pairing layer maps two peers for a relay session.
- A forwarding pipeline relays encrypted frames without modification.
- A monitoring subsystem captures metrics, health, and abuse events.

## Functional Requirements
- Accept inbound relay connections and pair peers by requested target UID.
- Maintain relay sessions without inspecting payload content.
- Enforce limits (max sessions, per-IP rate limits, idle timeouts).
- Provide live monitoring and basic admin controls (disconnect, ban).
- Operate as a full node for discovery and health signaling.

## Non-Functional Requirements
- Stable under sustained connections and concurrent relay sessions.
- No persistence of message contents.
- Clear operator visibility into relay state and abuse events.
- Minimal latency overhead beyond transport and forwarding.

## Protocol Outline (High Level)
- Relay uses the same handshake and framing as direct connections.
- Relay never decrypts payloads; it forwards frames as-is.
- Relay identifies peers by UID/identity metadata needed to pair sessions.
- Relay exposes DNS-like aliases instead of raw peer IPs.

## Relay Connection Lifecycle
1. Client connects to relay and performs normal handshake.
2. Client submits a relay request for target UID.
3. Relay matches a second client for the same session key or target UID.
4. Relay establishes a paired session and begins frame forwarding.
5. Session ends on disconnect, timeout, or admin action.

## Pairing and Routing Strategy
- Pairing key: requested target UID + session nonce.
- Only forward between two peers that mutually complete relay pairing.
- If the target peer is offline, keep a short-lived pending queue.
- Expire pending requests after a short timeout to avoid resource leaks.
- Peers see relay-assigned DNS-like aliases rather than raw IPs.

## Data Flow (High Level)
- Incoming framed data is copied to the paired socket without modification.
- Backpressure and throttling protect relay from slow consumers.
- Relay never logs or stores payload bytes.

## Relay Message Types (High Level)
- RelayHello: client identifies itself and requests relay service.
- RelayPairRequest: target UID and session nonce.
- RelayPairAck: relay confirms pairing and begins forwarding.
- RelayError: error codes for invalid target, timeout, or overload.

## Relay Protocol Flow (Draft)
1. Client connects to relay TCP port.
2. Client completes the standard Zer0Talk handshake and identity announce.
3. Client sends RelayHello with its UID and relay capabilities.
4. Client sends RelayPairRequest with target UID and session nonce.
5. Relay validates limits and places client into a pending queue.
6. When the target UID connects and requests pairing, relay links the two streams.
7. Relay responds with RelayPairAck and begins forwarding frames.
8. Relay forwards bytes as-is and does not parse application payloads.
9. Session ends on disconnect, timeout, or admin action.

## Address Obfuscation
- Relay assigns a DNS-like alias (for example, relay-<session-id>.zt) to each session.
- Clients surface only the alias in UI and logs.
- Aliases are relay-local and expire with the session.

## Alias Rules (Concrete)
- Format: `relay-<region>-<shortid>.zt`.
- `region`: 2-4 lowercase letters (e.g., `na`, `eu`, `ap`, `usw`).
- `shortid`: 12 lowercase base32 chars (crockford) derived from session id + random salt.
- Uniqueness: must be unique per active relay instance.
- Lifetime: valid only for the session; regenerate on reconnect.
- Display: clients must show alias only, never raw IPs, in UI and logs.

## Error Codes (Draft)
- RELAY_ERR_OVERLOAD: relay capacity exceeded.
- RELAY_ERR_UNAVAILABLE: target UID not available within timeout.
- RELAY_ERR_FORBIDDEN: blocked UID or IP.
- RELAY_ERR_BAD_REQUEST: malformed relay request.
- RELAY_ERR_INTERNAL: unexpected relay failure.

## Proposed Project Layout
- Zer0Talk.RelayServer/
  - Program.cs (entrypoint, host wiring)
  - RelayApp.axaml (UI shell)
  - RelayApp.axaml.cs
  - Services/
    - RelayListener.cs (socket accept loop)
    - RelaySessionManager.cs (pairing + lifecycle)
    - RelayForwarder.cs (stream copy with backpressure)
    - RelayRateLimiter.cs (per-IP and per-UID limits)
    - RelayMetrics.cs (counters, gauges)
  - ViewModels/
    - RelayDashboardViewModel.cs
    - RelayConnectionsViewModel.cs
  - Views/
    - RelayDashboardView.axaml
    - RelayConnectionsView.axaml
  - Utilities/
    - RelayLogging.cs
    - RelayConfig.cs

## Core Components
- Listener: Accepts incoming TCP sessions.
- Session Pairing: Matches two peers for a relay session.
- Forwarder: Copies framed data between paired sockets.
- Health Monitor: Timeouts, keepalives, and resource limits.
- Admin UI: Connection list, pairing state, throughput, errors, and bans.

## Monitoring and Admin UI
- Live connection table: peer UID, relay session state, bytes in/out.
- Events view: connect/disconnect, errors, abuse triggers.
- Health indicators: CPU/memory, sockets, backlog, queue depth.
- Admin actions: disconnect session, temporary ban, clear pending queue.

## Abuse Controls and Hardening
- Rate limits per IP and per UID (burst + sustained).
- Max concurrent sessions per IP and total.
- Idle session timeouts and stalled-forward detection.
- Temporary bans on repeated handshake failures or flooding.
- Optional allowlist for private relay deployments.

## Security Notes: Netstat/Wireshark Visibility
- It is not possible to prevent OS tools (netstat, Wireshark) from showing active endpoints on a host.
- Viable mitigations:
  - Relay-only routing mode: no direct P2P connections when privacy is prioritized.
  - Avoid exposing peer IPs in application UI or logs.
  - Optional VPN or Tor for operators who need additional network privacy.

## Configuration
- Relay host/port configurable; defaults defined in server settings (port 443).
- Client uses AppSettings.RelayServer and RelayFallbackEnabled.
- Connection limits and rate limits configurable per relay instance.
- Relay should publish its version string in logs and status UI using the client version format.

## Versioning and About
- About dialog must show version and prototype tag (e.g., "In-Dev Alpha v0.0.2.09").
- Top window chrome should display the orange tag next to the version number.
- Relay build pipeline should auto-increment the build number on successful builds.

## Deployment Targets
- Windows 11 Pro/Enterprise (desktop UI mode).
- Windows Server versions that support .NET 9.
- Optional Windows Service mode for auto-start on boot.
- Optional headless mode later, if needed for server environments.

## Service Mode (Optional)
- Service mode runs the relay in the background and auto-starts with Windows.
- Logs are written to the same rotating log location as desktop mode.
- If the UI is installed, it can attach to the running service for monitoring.
- Service mode must not require interactive desktop access.
- Provide a system tray icon that can open the console UI and trigger an emergency shutdown.

## System Tray Behavior
- Menu: Open Console, Show Status, Pause Relay, Resume Relay, Emergency Shutdown, Exit UI.
- Emergency Shutdown requires confirmation and writes a shutdown marker to logs.
- Tray actions should not require admin rights unless installing/updating the service.

## Tray UI Copy and Icon States
- Tooltip: "Zer0Talk Relay — Running" (Paused/Degraded/Error variants).
- Status text: "Active sessions: <n>" and "Pending: <n>".
- Icon states: Running, Paused, Degraded (rate-limited), Error (listener down).
- Emergency Shutdown dialog: "Stop the relay immediately? Active sessions will drop." Buttons: Stop Relay, Cancel.

## Localization Keys (Draft)
- Relay.Tray.Tooltip.Running
- Relay.Tray.Tooltip.Paused
- Relay.Tray.Tooltip.Degraded
- Relay.Tray.Tooltip.Error
- Relay.Tray.Status.ActiveSessions
- Relay.Tray.Status.PendingSessions
- Relay.Tray.Menu.OpenConsole
- Relay.Tray.Menu.ShowStatus
- Relay.Tray.Menu.PauseRelay
- Relay.Tray.Menu.ResumeRelay
- Relay.Tray.Menu.EmergencyShutdown
- Relay.Tray.Menu.ExitUi
- Relay.Tray.Dialog.EmergencyShutdown.Title
- Relay.Tray.Dialog.EmergencyShutdown.Body
- Relay.Tray.Dialog.EmergencyShutdown.Stop
- Relay.Tray.Dialog.EmergencyShutdown.Cancel

## Testing Plan
- Direct-failure fallback: verify relay success when direct connect fails.
- NAT stress: peers behind strict NAT can still connect via relay.
- Abuse simulations: confirm rate limits and bans trigger correctly.
- Soak test: long-lived sessions with sustained throughput.

## Open Questions
- Do we need UDP relay support in v1 or later?
- Should relay allow multi-peer fanout in the future?
- Do we need a separate admin auth layer for the UI?

## Logging
- Connection metadata only (no message content).
- Rotating logs with retention limits.
- Structured logs for session pairing and relay failures.

## Milestones
1. Relay server skeleton (listener, basic UI, embedded console).
2. Session pairing and frame relay pipeline (TCP only).
3. Monitoring dashboard with live connection stats.
4. Rate limiting and abuse controls.
5. Client integration testing for relay fallback.
6. Packaging and deployment (Windows 11 Pro+ / Server).
