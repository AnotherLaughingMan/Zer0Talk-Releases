# ZTalk Architecture Overview

ZTalk is a peer-to-peer messaging prototype built on Avalonia (.NET 9) and structured around the MVVM pattern. This guide walks through the major layers, services, and runtime flows so contributors can orient themselves quickly.

## High-Level Layout

| Area | Description | Key Paths |
| --- | --- | --- |
| Views | Avalonia XAML views and their code-behind shells. Keeps UI logic thin. | `Views/`, `Views/Controls/` |
| ViewModels | State containers and interaction glue for each window or component. | `ViewModels/` |
| Services | Long-lived background workers, IO, networking, and domain logic. | `Services/` |
| Containers | Persistent data stores (messages, settings sidecars, queues). | `Containers/` |
| Utilities | Reusable helpers, value converters, logging infrastructure. | `Utilities/` |
| Assets | Runtime resources: icons, avatars, sounds. | `Assets/` |
| Styles | Theme overrides, palette tweaks, cross-view shared styles. | `Styles/` |

### Runtime Flow

1. **Startup**
   - `Program.cs` boots Avalonia and hands control to `App.axaml.cs`.
   - `StartupInit` wires core services, logging paths, and begins network discovery.
2. **Unlock Gate**
   - `LockService` controls the initial unlock overlay; `UnlockViewModel`
     coordinates with `SettingsService` to decrypt the user passphrase.
3. **Main Window Lifecycles**
   - `MainWindow.axaml` binds to `MainWindowViewModel`, which aggregates contact lists,
     timeline state, diagnostics, and network presence snapshots.
   - View models subscribe to `EventHub` to stay reactive without tight coupling.
4. **Networking Pipeline**
   - `NetworkService` + `PeerManager` handle peer discovery, handshake exchange, and
     direct message delivery using the NAT traversal helpers.
   - `OutboxService`, `MessageContainer`, and `RetentionService` coordinate queued
     payloads, local storage, and retention policies.
5. **Link & Media Intelligence**
   - `LinkPreviewService` runs background fetchers with throttled requests and local
     caching; its results flow back through the event hub.

## Core Services Cheat Sheet

| Service | Responsibility | Notables |
| --- | --- | --- |
| `SettingsService` | Secure storage of user configuration, passphrases, and DPAPI sidecars. | Encrypts to `%AppData%\Roaming\ZTalk\settings.p2e`. |
| `IdentityService` | Maintains local identity records, verified badges, and avatar linkage. | Works with `AvatarCache` and `UidToAvatarConverter`. |
| `NetworkService` | Manages sockets, discovery, NAT traversal, and peer lifecycle. | Interfaces with `PeerManager`, `PeerCrawler`, `NatTraversalService`. |
| `LockService` | Controls unlock overlay, blur behavior, and shutdown gating. | Avoids re-instantiating `MainWindow` by reusing the same shell. |
| `ThemeService` | Switches theme palettes on demand, applies style overrides. | Uses styles under `Styles/ThemeSovereignty*.axaml`. |
| `LinkPreviewService` | Fetches metadata + thumbnails for shared links. | Utilizes throttled background tasks and respects opt-outs. |
| `RetentionService` | Cleans and summarizes message history. | Operates alongside `MessagePurgeSummary`. |
| `EncryptionService` | Houses cryptographic primitives for transport & storage. | Stays versioned so consumers can audit and extend it. |

## Assets & Resources

- **Icons** live in `Assets/Icons/` and are wired through `ZTalk.csproj` as Avalonia
  resources. Replace the developer placeholders (`Icon.ico`, `Special/Encrypted.ico`,
  `Status/*.ico`) with production artwork when available.
- **Avatars** reside in `Assets/Avatars/` and map to peers via converters.
- **Sounds** are under `Assets/Sounds/` and referenced directly by the UI controls.

> ℹ️ The root `.gitignore` tracks icons and avatars explicitly so generated artwork ships with the repo while user-generated assets stay local.

## Logging & Diagnostics

- All diagnostics go to `bin/<Configuration>/net9.0/logs/`. The most commonly used
  files (`startup.log`, `network.log`, `error.log`, etc.) are easy to locate thanks to
  `Utilities/LoggingPaths`.
- Automation scripts under `scripts/` support publish pipelines, checkpointing, log
  sampling, and stress profiling.

## Next Steps for Contributors

1. **Review the ViewModels** relevant to the feature you want to touch—they expose the
   bindings you will see in XAML.
2. **Trace the supporting Services** to understand where data is coming from or which
   background job triggers updates.
3. **Update documentation** if you introduce a new service, resource, or script so the
   rest of the team can track the moving parts.

For build and workflow commands, see `docs/developer-guide.md` (or create it if it
doesn’t exist yet).```}```}Converted `2` to the Python type corresponding to the JSON schema (type `string`)FAILED. Fix this and try again.}