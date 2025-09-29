# context.md — P2PTalk v0.0.2

## Overview
P2PTalk is a desktop-only, peer-to-peer messaging application built in C# using Avalonia UI. It prioritizes privacy, modular architecture, and encrypted local storage. There is no central server, no cloud sync, and no mobile support. All features are scoped for sovereign, non-destructive workflows.

## Framework
- Language: C#
- UI Framework: Avalonia UI v11.3.0
- Runtime: .NET 9.0.304
- IDE: Visual Studio Code (preferred)
- Platforms: Windows x64, Linux x64
- No mobile support

## Architecture
- Peer-to-peer mesh network (inspired by ZeroNet)
- No central server; all nodes route messages
- UID-based contact discovery (8-character alphanumeric)
- Manual contact list management
- Peer list is ephemeral and auto-pruned
- Optional “Major Node” designation for manual port setup

## User Account Creation
- On first launch, the app prompts the user to create a local account
- Account creation includes:
  - Username (display name)
  - UID (auto-generated, 8-character alphanumeric)
  - Passphrase (used for encryption key derivation via Argon2id)
- Display name can be set during account creation or updated later via the Settings window
- No email, phone number, or external identifiers are collected
- Account data is stored locally in an encrypted `.p2e` container
- UID is used for peer discovery and contact linking
- Passphrase gates access to all encrypted data and unlocks the app
- Only one account is allowed per device
- Multiple local accounts are explicitly not supported due to privacy and security concerns
- The account creation window is a standalone native window, not slaved to `MainWindow`
- It must be appropriately sized—neither fullscreen nor cramped—with layout optimized for clarity and input ergonomics

## Window Architecture
- All configuration windows are implemented as native windows, not modal dialogs
- `SettingsWindow` handles:
  - Display name updates
  - Passphrase changes
  - Theme selection
  - Chat retention settings
- `NetworkWindow` handles:
  - Port configuration
  - Major Node designation
  - Peer diagnostics
- These windows are launched independently from the main UI and are not slaved to `MainWindow`
- This separation ensures modularity, avoids UI blocking, and preserves user control

## Theme Configuration
- Use Avalonia FluentTheme
- Theme dropdown with:
  - Dark (default)
  - Light
  - Auto (system detection)
- Store theme preference locally and apply on startup
- Switching themes must update UI dynamically
- Dark Mode must use deep charcoal or graphite tones—not pure black

## Data Storage
- All data stored locally and encrypted
- Use `.p2e` container files for:
  - Contact list
  - Message history
  - Peer cache
  - App settings
- Store data in `%AppData%\Roaming\P2PTalk` (Windows)
- Avoid triggering Windows Defender or UAC
- No cloud sync or remote backups

## Encryption Protocol
- Use **XChaCha20-Poly1305** for symmetric encryption
- Keys derived from user passphrases using **Argon2id**
- Nonces must be 192-bit and randomly generated per session or container
- All encrypted data must be authenticated with Poly1305 MAC
- Encryption logic implemented in `EncryptionService`
- Post-quantum key exchange may be added later in a separate module

## Chat Retention
- Implement retention timer feature
- User-configurable limits (e.g. 1 hour, 1 day, 1 week)
- Messages older than limit are auto-deleted
- Retention settings stored in encrypted `.p2e` container
- Timer logic runs periodically or on startup
- Default: no auto-deletion unless configured

## Window State & Settings Persistence
- All windows in P2PTalk are resizable and independently positioned
- The app must save:
  - Window size (width and height)
  - Window position (screen coordinates)
  - Maximized or normal state
- These values are stored locally in the encrypted `.p2e` container
- Settings must persist across sessions and be restored on launch
- No fallback to default sizes unless the saved state is missing or corrupted
- Settings include:
  - Theme preference
  - Display name
  - Chat retention timer
  - Network configuration
  - Lock behavior
- All settings are scoped to the single local account and encrypted

## Folder Structure
- `/Views` — XAML UI files
- `/ViewModels` — UI logic
- `/Models` — data structures
- `/Services` — networking, encryption, lock logic
- `/Containers` — encrypted `.p2e` file handling
- `/Utilities` — logging and helper functions

## Required Classes to Scaffold
- `PeerManager` — manages peer list, auto-prune logic
- `ContactManager` — handles persistent contacts
- `MessageContainer` — encrypts and stores messages in `.p2e` files
- `EncryptionService` — handles encryption and key derivation
- `NetworkService` — manages UDP/TCP connections and routing
- `LockService` — handles passphrase gating and auto-lock behavior
- `ThemeService` — manages theme selection and runtime switching
- `RetentionService` — enforces message expiration based on user-defined timer

## Coding Instructions for GitHub Copilot
- Scaffold all placeholder classes listed above
- Replace placeholder methods with real implementations
- Use Avalonia XAML for layout
- Preserve modularity and favor non-destructive edits
- Add structured logging for network and encryption operations
- Avoid platform-specific APIs unless scoped and documented
- Preserve namespace casing: `P2PTalk`
- Do not scaffold mobile support
- Do not use cloud storage
- Do not auto-populate contact list
- Encryption must be enforced for all stored data

## Notes
This file replaces `instructions.md` and serves as the blueprint for project scaffolding, implementation, and architectural boundaries. All generated code must align with the privacy-first, modular design of P2PTalk.
