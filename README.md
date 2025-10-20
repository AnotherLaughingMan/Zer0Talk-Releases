# ZTalk

# ZTalk

**Secure Peer-to-Peer Messaging for Windows**

ZTalk is a privacy-focused, decentralized messaging application that puts you in complete control of your communications. With no central servers, your conversations stay between you and your contacts—encrypted, local, and truly private.

## ✨ Key Features

### 🔐 Privacy & Security First
- **End-to-End Encryption:** All messages are encrypted using industry-standard cryptography
- **Peer-to-Peer Architecture:** Direct connections—no servers storing your data
- **Local Storage:** Your messages, contacts, and settings stay on your machine
- **Secure File Wiping:** Enhanced privacy with military-grade secure deletion
- **Identity Verification:** Verify your contacts with trusted identity badges
- **IP Blocking:** Built-in security to block unwanted connections

### 💬 Rich Messaging Experience
- **Real-Time Chat:** Instant peer-to-peer messaging with delivery confirmation
- **Message Editing:** Edit sent messages with automatic propagation to recipients
- **Message Deletion:** Remove messages from both sides of the conversation
- **Markdown Support:** Rich text formatting with code syntax highlighting
- **Link Previews:** Automatic preview generation for shared URLs
- **Message Burn:** Enhanced ephemeral messaging for sensitive content

### 👥 Contact Management
- **Contact Requests:** Send and receive contact invitations
- **Presence Indicators:** See when contacts are online, idle, or in Do Not Disturb mode
- **Custom Avatars:** Personalize your identity with custom profile pictures
- **Verified Contacts:** Badge system to identify verified trusted contacts
- **Smart Grouping:** Automatic organization of online and offline contacts

### 🎨 Customization & Themes
- **Advanced Theme Engine:** Full visual customization of the application
- **Theme Editor:** Create and export custom themes with visual preview
- **Gradient Editor:** Design custom gradient backgrounds and accents
- **Color Palette Editor:** Fine-tune every color in the interface
- **Dark/Light Modes:** Built-in themes optimized for different lighting
- **Import/Export Themes:** Share your custom themes with the community

### 🔔 Notifications & Sounds
- **Desktop Notifications:** Stay updated with system tray notifications
- **Custom Sounds:** Audio alerts for messages and events
- **Do Not Disturb:** Silence notifications when you need focus
- **System Tray Integration:** Minimize to tray for background operation

### ⚙️ Additional Features
- **Lock Screen:** Secure your conversations with a custom lock overlay
- **Framerate Optimization:** Intelligent performance tuning for resource efficiency
- **Auto-Start:** Launch ZTalk automatically with Windows
- **Comprehensive Logging:** Detailed diagnostics for troubleshooting
- **Settings Encryption:** Secure local settings storage with DPAPI

## 📥 Installation

### Quick Install (Recommended)
1. Download the latest release from the [Releases](../../releases) page
2. Download both files:
   - `ZTalk-vX.X.X.XX-Alpha-Setup.exe`
   - `ZTalk-vX.X.X.XX-Alpha-win-x64-Release-single-YYYYMMDD-HHMMSS.zip`
3. Place both files in the same folder
4. Run the Setup.exe as Administrator
5. Follow the installation wizard

### Manual Installation
1. Download the standalone zip package
2. Extract to your preferred location
3. Run `ZTalk.exe`

## 🖥️ System Requirements

- **OS:** Windows 10 or Windows 11 (64-bit)
- **Architecture:** x64 only
- **Disk Space:** ~200 MB
- **Memory:** 512 MB RAM recommended
- **.NET Runtime:** Not required (bundled in standalone builds)

## 🚀 Getting Started

1. **First Launch:** On first run, ZTalk will guide you through initial setup
2. **Create Identity:** Set your display name and optional passphrase
3. **Add Contacts:** Share your ZTalk ID with friends or enter theirs
4. **Start Chatting:** Once connected, your messages are end-to-end encrypted

## 📡 Network & Connectivity

ZTalk uses peer-to-peer connections, which means:
- Both users must be online simultaneously to exchange messages
- Network firewalls may need port forwarding configuration
- Local network connections work seamlessly
- Internet connections require proper router configuration

## 🔒 Privacy & Data

- **No Servers:** Your data never touches a central server
- **No Tracking:** We don't collect analytics, telemetry, or usage data
- **Local Storage:** All data stored in `%AppData%\Roaming\ZTalk\`
- **Encrypted Settings:** Settings file is encrypted using Windows DPAPI
- **You Own Your Data:** Export, backup, or delete anytime

## ⚠️ Alpha Release Notice

ZTalk is currently in **ALPHA** status. This means:
- The software is functional but may contain bugs
- Features are subject to change
- Regular updates may introduce breaking changes
- Not recommended for mission-critical communications
- Please backup your data regularly

## 🐛 Reporting Issues

If you encounter bugs or have feature requests, please check existing issues first, then open a new issue with:
- Detailed description of the problem
- Steps to reproduce
- Your Windows version and ZTalk version
- Relevant log files (found in the installation directory)

## 🤝 Community & Support

- **GitHub Issues:** Bug reports and feature requests
- **Discussions:** Questions and community support
- **Updates:** Follow the repository for release announcements

## 📄 License

Copyright (c) 2025 AnotherLaughingMan. All rights reserved.

This software is provided for personal use. See LICENSE.md for complete terms.

## 🙏 Acknowledgments

Built with:
- [Avalonia UI](https://avaloniaui.net/) - Cross-platform .NET UI framework
- [.NET 9.0](https://dotnet.microsoft.com/) - Modern application platform
- Various open-source libraries (see project dependencies)

---

**Download the latest version from the [Releases](../../releases) page and join the peer-to-peer revolution!** 🚀

Peer-to-peer messaging experiment for Windows, built with Avalonia (.NET 9) and a service-heavy MVVM architecture. Everything runs locally: encrypted settings, identity sidecars, and contact data live on your machine—no central servers involved.

## Snapshot of the Current Feature Set

- **Peer-to-peer chat:** Direct messaging powered by the `NetworkService`, `PeerManager`, and `PeerCrawler` pipeline.
- **Identity & verification:** `IdentityService`, encrypted passphrases, verified badges, and presence avatars for each peer.
- **Rich presence:** Idle/DND awareness, grouped online/offline lists, and custom converters for avatars and status.
- **Encrypted storage:** `SettingsService` uses DPAPI passphrase sidecars; message containers keep history local.
- **Link intelligence:** `LinkPreviewService` fetches metadata in the background with throttled caching.
- **Lock screen & focus tools:** `LockService` overlays, blur gating, and `FocusFramerateService` for resource smoothing.
- **Theme swaps on the fly:** Palette overrides via `ThemeService` with dark/light accent variants.
- **Diagnostics baked-in:** Structured logging with rotation, checkpointable builds, and automation scripts for publishing.

## Known Issues & Issue Reporting

- Nearly every subsystem is mid-flight; regressions are expected and tracked internally.
- GitHub Copilot has already performed multiple audits—we’re aware of the major pain points.
- **Please don’t open new issue reports.** Doing so duplicates known bugs and slows development; we’ll announce when outside feedback is actionable again.

## Getting Started

```bash
# Build
dotnet build .\ZTalk.csproj

# Run
dotnet run --project .\ZTalk.csproj
```

### Requirements

- Windows 10/11 (x64) with the .NET 9 SDK installed.
- PowerShell 7+ for the automation scripts (they handle ExecutionPolicy themselves).

## Configuration & Data Paths

- Settings: `%AppData%\Roaming\ZTalk\settings.p2e` (encrypted at rest; auto-migrated from older paths).
- Logs: `bin/<Configuration>/net9.0/logs/` (see `Utilities/LoggingPaths`).
- Publish output: `publish/win-x64-<Variant>/` with timestamped zips.

## Build & Release Automation

- `scripts/alpha-strike.ps1` — multi-variant publish, optional single-file debug, retention management.
- `scripts/publish-debug.ps1` — fast Debug/Release publish with optional single-file bundles.
- `scripts/checkpoint-build.ps1` — generates checkpoint manifests and prunes older artifacts.
- `scripts/verify-checkpoint.ps1` — validates manifests; `-Strict` fails on mismatch.
- `scripts/memory-profile.ps1` & `scripts/memory-stress.ps1` — profiling helpers when tuning allocations.

## Documentation

- [Architecture Overview](docs/architecture.md) — how services, view models, and resources fit together.
- [Developer Guide](docs/developer-guide.md) — build commands, scripts, and repo hygiene tips.
- CODEOWNERS and a sensitive file audit workflow enforce reviews for critical assets; see the Developer Guide for details.

## Contributing

- Pull requests are welcome for targeted fixes and feature spikes, but expect fast-moving branches.
- Skip filing issues; channel feedback through direct discussion until the prototype stabilizes.

## License

This repository is currently closed-source for distribution purposes; reach out to the maintainers for reuse questions.
