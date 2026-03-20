# Zer0Talk: Secure Peer-to-Peer Messaging for Windows

**Privacy-First, Decentralized Chat – No Servers, No Tracking, Just You and Your Contacts**

[![Latest Release](https://img.shields.io/github/v/release/AnotherLaughingMan/Zer0Talk-Releases?label=latest%20release&style=flat-square&color=blueviolet)](https://github.com/AnotherLaughingMan/Zer0Talk-Releases/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue?style=flat-square)](https://github.com/AnotherLaughingMan/Zer0Talk-Releases/releases/latest)
[![Status](https://img.shields.io/badge/status-Alpha-orange?style=flat-square)](https://github.com/AnotherLaughingMan/Zer0Talk-Releases/releases/latest)

Zer0Talk is a fully decentralized messaging app designed for users who value true privacy. Built on peer-to-peer (P2P) technology, it ensures your conversations stay encrypted and local, with no central servers collecting your data. Whether you're chatting with friends or sharing sensitive information, Zer0Talk puts control back in your hands.

This is an **alpha release** – it's functional but expect bugs and changes. Not for critical use yet!

---

<img width="1176" height="757" alt="image" src="https://github.com/user-attachments/assets/1c6232a9-1cd4-40ae-8cca-9a30c83fae23" />

---

## ✨ Why Choose Zer0Talk?

- **Ultimate Privacy**: End-to-end encryption with modern, multi-layered cryptography – far beyond basic protections.
- **No Central Authority**: Direct P2P connections mean no company or government can subpoena your chats or force age/ID verification.
- **Connects Automatically**: Three-tier connection system tries direct, then NAT traversal, then relay – usually without any manual setup.
- **Rich User Experience**: Rich text formatting, message editing/deletion, Burn Conversation, and customizable themes.
- **Resistant to Overreach**: In a world of creeping data mandates (e.g., California AB-1043, UK OSA), Zer0Talk collects **nothing** – no accounts, no metadata, no way to tie messages to identities or ages.

If you're tired of platforms like Discord, Reddit, or even OS-level prompts bending to regulations, Zer0Talk is your escape to uncensorable communication.

---

## 🔑 Key Features

### 🔒 Security & Privacy
- **End-to-End Encrypted**: Every message is encrypted between your device and your contact's device. Nobody in between — not even a relay server — can read your messages.
- **Local-Only Storage**: All data (messages, contacts, settings) stays on your device in `%AppData%\Roaming\Zer0Talk\`. Nothing is uploaded anywhere.
- **Protected Passphrases**: Your passphrase is protected by Windows DPAPI locally; messages use purpose-built modern crypto (XChaCha20-Poly1305 + Argon2id KDF).
- **Identity Verification**: Cryptographic identity badges for trusted contacts; block by key fingerprint or IP.
- **Secure Deletion**: Drive-aware wiping when you burn or delete messages.
- **Zero Data Collection**: No telemetry, analytics, logs, or tracking ever leaves your machine.
- **No File or Attachment Transfers**: By design and policy. This eliminates entire classes of exploitation risk.

### 💬 Messaging
- **Real-Time P2P Chat**: Instant delivery with delivery confirmations (both users must be online simultaneously).
- **Edit & Delete**: Modify or remove messages on both ends.
- **Burn Conversation**: Permanently and unrecoverably wipe all your sent messages from a conversation with one click.
- **Markdown & Formatting**: Bold, italic, code blocks, and more.
- **Message Status Indicators**: Clock (pending), single checkmark (sent), filled checkmark (delivered).

### 👥 Contacts & Presence
- **Simple Contact Adding**: Share your Zer0Talk ID; contacts request and connect directly.
- **Avatars & Status**: Custom profile pictures, online/idle/DND indicators.
- **Smart Contact List**: Sorted by presence and last message time, with unread badge, last message preview, and connection mode indicator (direct or relay).
- **Search**: Filter contacts by name or ID in real time.

### 🌐 Connectivity (Automatic – No Setup Required for Most Users)
- **Tier 1 – Direct**: If both users are on the same LAN or the connection can reach you directly, Zer0Talk connects without any intermediate infrastructure.
- **Tier 2 – NAT Traversal**: Zer0Talk automatically attempts UPnP/PCP port mapping through your router. If successful, peers across the internet can connect directly to you.
- **Tier 3 – Relay Fallback**: If direct and NAT both fail, the connection routes through a relay server. The relay is a blind TCP pipe — it forwards encrypted bytes and cannot read your messages. End-to-end encryption is fully maintained.

Zer0Talk tries all three tiers and uses whatever works. For most users this just works. Power users can self-host the relay server for full infrastructure control.

### 🎨 Customization
- **Themes**: Built-in dark and light modes; full theme editor with colors, accents, and gradients. Export and share themes.
- **Notifications**: System tray alerts, custom sounds, Do Not Disturb.
- **Smooth Scrolling**: Optional animated scrolling toggle.

### ⚙️ Extras
- **Lock Screen**: Passphrase-protected overlay to secure the app when stepping away.
- **Privacy Policy**: In-app viewer with acceptance tracking.
- **Auto-Start**: Runs in background with tray icon.
- **Performance Controls**: Framerate tuning for older hardware.

---

## 📋 System Requirements

- **OS**: Windows 10 or 11 (64-bit only).
- **Hardware**: x64 architecture; 512 MB RAM recommended; ~200 MB disk space.
- **Dependencies**: None – .NET 9 is bundled in the installer.

---

## 📥 Installation

Zer0Talk is a standalone Windows app — no app store, no account creation, no server sign-up. Just download and run.

### Recommended: Quick Installer (One-Click Setup)
1. Go to the [Releases page](https://github.com/AnotherLaughingMan/Zer0Talk-Releases/releases/latest).
2. Download the latest installer (`.exe`).
3. Right-click → **Run as administrator** (required first time for firewall rules).
4. Follow the wizard. It bundles .NET 9 — no separate runtime install needed.
5. Launch from the Start Menu or desktop shortcut.

### Alternative: Portable ZIP (No Install)
1. From the same Releases page, download the `.zip` archive.
2. Extract to any folder (e.g., `C:\PortableApps\Zer0Talk`).
3. Right-click `Zer0Talk.exe` → **Run as administrator** on first launch.
4. No install traces left behind — fully portable.

### SmartScreen Warning
New apps from GitHub trigger Windows SmartScreen:
- Click **More info** → **Run anyway**.
- Or: Right-click the `.exe` → Properties → General tab → check **Unblock** → Apply.

See `docs/install-run-troubleshooting.md` for screenshots and permanent fixes. This is a known Windows behavior for apps not issued with a paid code-signing certificate — not a sign of a problem with Zer0Talk.

**Auto-updates**: Enabled by default. The app notifies you of new versions and handles downloads.

---

## 🚀 Getting Started – First Launch

Zer0Talk walks you through setup on first run. Everything happens locally — no internet sign-up, no data sent anywhere during setup.

### 1. Launch the App
Open Zer0Talk. You'll see the **Sign In** screen.

### 2. Create Your Identity (First Time Only)
- The app generates a **strong random passphrase** for you automatically (shown once).
- **Write it down or store it securely offline immediately** — see the Passphrase section below.
- Set your **Display Name** (what contacts see — changeable later).
- Optionally add an avatar.
- Accept the Privacy Policy when prompted.

### 3. Your Zer0Talk ID
After signing in, your Zer0Talk ID is shown on the main screen. This is what you share with people who want to contact you. It's derived from your public key — unique and cryptographically verifiable.

### 4. Add a Contact
- Click **+ Add Contact**.
- Enter your contact's Zer0Talk ID.
- Send the request — they accept on their end.
- Once accepted and both of you are online, chat opens automatically. Messages are E2E encrypted from the first frame.

### 5. Start Chatting
Select a contact, type, send. Edit or delete messages, burn conversations, use Markdown formatting. Connection happens automatically — direct if possible, relay as fallback.

**Note**: Both you and your contact must be **online at the same time** to exchange messages. Outgoing messages are held **locally and encrypted** until your contact comes online — nothing is queued on a server.

**Quick Tips:**
- Customize early: Settings → Themes for colors and gradients. In-app tray notifications use your theme colors; desktop (system) notifications use your OS defaults.
- Back up your data: copy `%AppData%\Roaming\Zer0Talk\` to an encrypted external drive regularly.
- If a connection shows as "relay" instead of "direct", that's normal and fully encrypted — see the Network section if you want to optimize for direct.

## 🔐 Passphrase: What You Need to Know

Your passphrase is automatically generated at account creation. It unlocks the app and protects all your local data. This is not a password you choose — it's a long random string for maximum security.

**What to do immediately after setup:**

Store your passphrase securely offline before anything else. Good options:
- **Encrypted USB drive**: Save to a text file on a BitLocker or VeraCrypt-encrypted drive. Store the drive somewhere safe.
- **Paper backup**: Handwrite the passphrase on paper and store it in a locked or fireproof location.
- **Password manager**: A reputable local or offline password manager works well.

**Never**: Store it unencrypted digitally (email, cloud notes, unencrypted text files, phone screenshots).

### If You Lose Your Passphrase

1. On the sign-in screen, click **Lost Passphrase?**.
2. The app generates a brand-new passphrase and displays it.
3. **Store the new one immediately** before clicking anything else.
4. Enter it when prompted. You're back in.

Recovery is **100% local** — no servers, no internet, no remote verification. Your existing contacts, messages, and identity are preserved (they re-encrypt under the new passphrase).

**Backup your data folder too**: Copy `%AppData%\Roaming\Zer0Talk\` to an encrypted external drive periodically. It's self-contained — everything needed to restore is in that folder (plus your passphrase).

---

## 📡 Network & Connectivity

### How Zer0Talk Connects

Zer0Talk uses a three-tier system, tried in order automatically:

**Tier 1 — Direct Connection**  
Zer0Talk listens on TCP port **26264** (default). If your contact's IP is reachable — same LAN, or both on open internet — a direct TCP connection is made. No routing infrastructure involved.

**Tier 2 — NAT Traversal (UPnP/PCP)**  
Behind a home router? Zer0Talk automatically requests a port mapping using UPnP or PCP. When this succeeds, you're reachable from the internet without any manual router configuration. The app registers your external address with the rendezvous service so contacts can find you.

If UPnP isn't available on your router, you can set up manual port forwarding for TCP 26264 (see below).

**Tier 3 — Relay Fallback**  
If neither direct nor NAT traversal works, Zer0Talk automatically falls back to routing the connection through a relay server. The relay is a **blind TCP pipe** — it forwards the encrypted byte stream but cannot read any content. Your end-to-end encryption is fully intact through the relay.

A relayed connection is functionally identical to a direct one from a privacy standpoint. The relay only handles the underlying transport — it is provably blind to all plaintext.

### When to Do Manual Port Forwarding

If the app shows UPnP failed and you want direct connections instead of relay:

1. Run `ipconfig` in a terminal → note your IPv4 address.
2. Log into your router admin panel (usually `192.168.1.1` or `192.168.0.1`).
3. Find Port Forwarding and add a rule: **TCP port 26264**, external and internal, pointed at your IPv4 address.
4. In Windows Firewall, allow `Zer0Talk.exe` on both Private and Public networks.

This is optional. The relay fallback means you can always communicate even without port forwarding.

### Self-Hosting a Relay Server

For users or organizations who want full control over their infrastructure, Zer0Talk includes a self-hostable relay server (`Zer0Talk.RelayServer`). The relay never sees message content and retains no data. Multiple relay servers can federate — clients on different relays can communicate transparently.

See `Zer0TalkRelay/relay-config-guide.md` in the relay data folder for configuration details.

---

## 🛠️ Troubleshooting

### App Won't Launch or Install

**SmartScreen / "Windows protected your PC"**  
Click **More info** → **Run anyway**. This is a known Windows behavior for apps without expensive code-signing certificates — not a sign of a problem with Zer0Talk.  
Or: Right-click `.exe` → Properties → General → check **Unblock** → Apply.  
Full guide with screenshots: `docs/install-run-troubleshooting.md`.

**Antivirus blocks**  
Add the Zer0Talk install folder to your antivirus exclusions. False positives on unsigned P2P apps are common.

**Crashes on startup**  
Run as Administrator. If it persists, check Event Viewer (Win + R → `eventvwr` → Windows Logs → Application) for the specific error.

---

### Connectivity Problems

**Both users must be online simultaneously** — there is no offline message queuing.

**Connection is relayed but you'd prefer direct**  
- Check Settings → Network for NAT/UPnP status.
- Enable UPnP on your router if it's off, or set up manual port forwarding for TCP 26264.
- A relayed connection is fully functional and fully encrypted — this is an optimization, not a workaround for a broken session.

**Can't connect at all (relay also failing)**  
- VPNs often interfere — try disabling your VPN.
- Mobile hotspot networks may block outbound TCP — switch to your home network.
- Ensure `Zer0Talk.exe` is allowed in Windows Firewall on both Private and Public networks.
- Verify port 26264 is open: [canyouseeme.org](https://canyouseeme.org) → enter 26264.
- Restart the app on both ends. Update to the latest version.

---

### Passphrase & Encryption

**"Invalid key" or messages not decrypting**  
Usually a corrupted local cache. Close the app, delete `%AppData%\Roaming\Zer0Talk\Cache` (safe — regenerates automatically), relaunch, and re-add the contact if needed.

**Lost your passphrase**  
Use **Lost Passphrase?** on the sign-in screen. Full details in the Passphrase section above.

---

### General

- Logs: `%AppData%\Roaming\Zer0Talk\Logs\`
- Lag/high CPU: Settings → Performance → lower framerate.
- UI glitches: switch to default theme → restart.
- Full user guide: `docs/user-guide.md`
- Report issues via GitHub Issues. Include: version, Windows build, steps to reproduce, relevant log excerpts.

---

## ⚠️ Alpha Status

- Bugs are possible — report via GitHub Issues.
- Breaking changes may occur between updates — back up `%AppData%\Roaming\Zer0Talk\` before upgrading.
- Encryption is robust, but the app is not yet recommended for mission-critical communications.
- Keep auto-update enabled.

---

## 🤝 Community & Feedback

- **Issues & Feature Requests**: [GitHub Issues](https://github.com/AnotherLaughingMan/Zer0Talk/issues)
- **Discussions**: [GitHub Discussions](https://github.com/AnotherLaughingMan/Zer0Talk/discussions) for questions and ideas.
- Follow [@itsalaughingman](https://x.com/itsalaughingman) on X for updates.

---

## 📄 Legal

- **License**: Copyright © 2025-2026 Just a Laughing Man. All rights reserved. Personal use only — see [LICENSE.md](LICENSE.md).
- **Disclaimer**: See [Disclaimer.md](Disclaimer.md).
- **Privacy Policy**: See [docs/PRIVACY-POLICY.md](docs/PRIVACY-POLICY.md), also viewable in-app under Settings → About.
- **Security**: See [SECURITY.md](SECURITY.md) for the vulnerability reporting policy.
- **Code of Conduct**: See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

Built with Avalonia UI, .NET 9, and open-source cryptographic libraries (libsodium, Argon2id).

---

**Ready to go? Download from [Releases](https://github.com/AnotherLaughingMan/Zer0Talk-Releases/releases/latest) and connect freely — no verification theater required.**
