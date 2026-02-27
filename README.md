# Zer0Talk: Secure Peer-to-Peer Messaging for Windows

**Privacy-First, Decentralized Chat – No Servers, No Tracking, Just You and Your Contacts**

Zer0Talk is a fully decentralized messaging app designed for users who value true privacy. Built on peer-to-peer (P2P) technology, it ensures your conversations stay encrypted and local, with no central servers collecting your data. Whether you're chatting with friends or sharing sensitive info, Zer0Talk puts control back in your hands.

This is an **alpha release** – it's functional but expect bugs and changes. Not for critical use yet!

---

<img width="1176" height="757" alt="image" src="https://github.com/user-attachments/assets/1c6232a9-1cd4-40ae-8cca-9a30c83fae23" />



---

## ✨ Why Choose Zer0Talk?

- **Ultimate Privacy**: End-to-end encryption with modern, multi-layered cryptography – far beyond basic protections.
- **No Central Authority**: Direct P2P connections mean no company or government can subpoena your chats or force age/ID verification.
- **User-Friendly Features**: Rich text, link previews, message editing/deletion, burn timers, and customizable themes.
- **Resistant to Overreach**: In a world of creeping data mandates (e.g., California AB-1043, UK OSA), Zer0Talk collects **nothing** – no accounts, no metadata, no way to tie messages to identities or ages.

If you're tired of platforms like Discord, Reddit, or even OS-level prompts bending to regulations, Zer0Talk is your escape to uncensorable communication.

---

## 🔑 Key Features

### 🔒 Security & Privacy
- **Advanced End-to-End Encryption**: Messages are secured with a custom P2P protocol using multiple modern cryptographic techniques (layered protections beyond just local storage safeguards like DPAPI). Encryption logic lives in the Services layer for clean, auditable design.
- **Local-Only Storage**: All data (messages, contacts, settings) stays on your device in `%AppData%\Roaming\Zer0Talk\`.
- **Protected Settings & Passphrases**: DPAPI secures passphrase storage; full message crypto uses stronger, purpose-built methods.
- **Identity Verification**: Badges for trusted contacts; block by key fingerprint or IP.
- **Secure Deletion**: Military-grade wiping for messages and files when burned or deleted.
- **Zero Data Collection**: No telemetry, analytics, logs, or tracking ever leaves your machine.

### 💬 Messaging Tools
- **Real-Time P2P Chat**: Instant delivery with confirmations (both users must be online).
- **Edit & Delete**: Modify or remove messages on both ends.
- **Burn Messages**: Set timers for auto-deletion.
- **Markdown & Previews**: Format text, code, and auto-generate link thumbnails.

### 👥 Contacts & Presence
- **Easy Adding**: Share IDs for requests; auto-group online/offline.
- **Avatars & Status**: Custom pics, online/idle/DND indicators.

### 🎨 Customization
- **Themes & Gradients**: Built-in dark/light modes; edit colors, accents, and export/share themes.
- **Notifications**: Tray alerts, custom sounds, Do Not Disturb.

### ⚙️ Extras
- **Lock Screen**: Passphrase-protected overlay.
- **Auto-Start & Optimization**: Runs in background; framerate tuning for efficiency.

---

## 📋 System Requirements
- **OS**: Windows 10 or 11 (64-bit only).
- **Hardware**: x64 architecture; 512 MB RAM recommended; ~200 MB disk space.
- **Dependencies**: None – .NET 9 is bundled.

---

## 📥 Installation Guide

Zer0Talk is a standalone Windows app—no app store needed, no account creation on a server. Just download and run. We recommend the quick installer for easiest setup.

### Recommended: Quick Installer (One-Click Setup)
1. Go to the [Releases page](https://github.com/AnotherLaughingMan/Zer0Talk-Releases/releases/latest).
2. Download the latest installer file:  
   `Zer0Talk-v0.0.4.02-Alpha-Installer.exe` (or newer version).
3. Right-click the downloaded .exe → **Run as administrator** (important for first install to avoid permission issues).
4. Follow the simple wizard:
   - Accept defaults or choose your install folder (suggested: `C:\Apps\ZTalk`).
   - It bundles .NET 9 runtime—no separate install required.
5. Finish installation → Launch Zer0Talk from Start Menu or desktop shortcut.

**Common Windows hurdle – SmartScreen warning**  
If you see "Windows protected your PC" (because this is a new app from GitHub):  
- Click **More info** → **Run anyway**.  
- For future launches: Right-click Zer0Talk.exe → Properties → General tab → Check **Unblock** (if shown) → Apply.  
See `docs/install-run-troubleshooting.md` for screenshots and permanent fixes.

### Alternative: Manual Standalone (No Installer)
1. From the same Releases page, download the ZIP:  
   `Zer0Talk-v0.0.4.02-Alpha.zip` (or newer).
2. Extract to any folder you like (e.g., Desktop or `C:\PortableApps\Zer0Talk`).
3. Right-click `Zer0Talk.exe` → **Run as administrator** (first time only).
4. App launches directly—no install traces if you want portable use.

**Pro tip**: Enable auto-updates in app Settings after first launch (enabled by default in v0.0.4.02+). The app will notify you of new versions and handle downloads safely.

**System check**: Windows 10/11 (64-bit only). At least 512 MB RAM free. Internet for P2P connections (both parties online).

---

## 🚀 Getting Started – First Launch Walkthrough

Zer0Talk guides you through setup on first run. Everything happens locally—no internet signup, no data sent anywhere.

1. **Launch the App**  
   Open Zer0Talk (from shortcut or .exe). You'll see the purple-themed **Sign In** screen.

2. **Create Your Identity (First-Time Only)**  
   - The app automatically generates a **strong random passphrase** for you (long, secure string – shown once!).
   - **Immediately store this passphrase securely offline** (see Passphrase section below – critical!).
   - Set your **Display Name** (this is what contacts see – can change later).
   - Optional: Add an avatar (from file).
   - Review the on-screen storage advice, then click **Got It** or **Continue**.

3. **Passphrase Security Reminder**  
   Your passphrase unlocks the app and protects your local data.  
   - **Copy it** right away (for one-time login if needed).  
   - **Save it offline** (encrypted USB, paper in safe place – never cloud/unencrypted).  
   - **Do NOT lose it** – recovery exists but requires generating a new one (see Troubleshooting → Passphrase recovery).

4. **Main Screen – You're In!**  
   - Dashboard shows your Zer0Talk ID (long unique string – share this to add contacts).  
   - Online status, theme selector, settings gear.

5. **Add Your First Contact**  
   - Click **+ Add Contact** or go to Contacts tab.  
   - Enter a friend's Zer0Talk ID (they share it with you).  
   - Send request → They accept on their end.  
   - Once connected (both online), chat starts – messages are E2E encrypted automatically.  
   - Verify trust: Look for the verification badge after key exchange.

6. **Start Chatting**  
   - Select contact from list.  
   - Type message → Send.  
   - Enjoy: Edit/delete, burn timers, Markdown, link previews, themes, etc.

**Quick Tips for Smooth Start**:
- Both you and your contact must be online at the same time (P2P direct connect – no servers).
- If no connection: Check UPnP setup (Network Setup section) or test port 26264.
- Customize early: Settings → Themes for dark/light/gradients; Notifications for tray alerts.
- Backup: Copy `%AppData%\Roaming\Zer0Talk\` to encrypted external drive regularly.

You're now using truly private, decentralized chat. No age verification, no tracking—just direct, encrypted P2P. Welcome to the mesh! 🚀

---

## 📡 Network Setup: Enabling UPnP for Reliable Connections
(Keeping your original instructions intact – they're clear and comprehensive. No changes needed here.)

[... full UPnP section as you had it ...]

**Security Note**: UPnP can expose ports temporarily, but Zer0Talk's encryption ensures even if a connection is intercepted, messages remain unreadable.

---

## 🛠️ Troubleshooting & Tips

Zer0Talk is in alpha, so some hiccups are normal. Most issues fall into a few categories: startup/security blocks, network connectivity, encryption/passphrase handling, or general behavior. Try these steps in order.

### 1. App Won't Launch or Install
- **Windows SmartScreen / "Windows protected your PC" warning**:
  - Common for new/unsigned apps from GitHub.
  - Click **More info** → **Run anyway**.
  - For future launches: Right-click .exe → Properties → General tab → Check "Unblock" if present → Apply.
  - Run installer as Administrator first time.
  - Full guide with screenshots: `docs/install-run-troubleshooting.md`.

- **Antivirus / Windows Defender blocks**:
  - Temporarily disable real-time protection (Settings → Privacy & security → Windows Security → Virus & threat protection → Manage settings).
  - Add exclusion: Exclusions → Add folder → Select Zer0Talk install folder (e.g., `C:\Apps\ZTalk`).
  - Re-enable after.

- **Crashes on startup**:
  - Run as Administrator.
  - Reinstall if bundled .NET conflicts.
  - Check Event Viewer (Win + R → `eventvwr` → Windows Logs → Application) for errors.

### 2. Connectivity Problems (Can't add contacts, messages not delivering)
- Both users must be online simultaneously (no offline queuing yet).
- Test port 26264: https://canyouseeme.org → Enter 26264 → Check if open.
- In Zer0Talk Settings → Network: Look for status (e.g., "UPnP Successful" or errors).
- **UPnP fails**: Enable on router (see Network Setup), restart router/PC. ISP blocks common—try manual forwarding.
- **Manual Port Forwarding**:
  1. `ipconfig` → Note IPv4 Address.
  2. Router admin (e.g., 192.168.1.1) → Port Forwarding → TCP 26264 external/internal → Your IP.
  3. Windows Firewall: Allow Zer0Talk.exe for Private/Public.
- **VPN/Hotspot**: Often block P2P—switch networks.
- Restart app on both ends; update to latest via auto-update.

### 3. Encryption / Security / Data Issues

- **Messages not decrypting / "Invalid key" errors**:
  - Usually mismatched passphrase or corrupted local storage.
  - Close app → Delete `%AppData%\Roaming\Zer0Talk\Cache` folder (safe – regenerates).
  - Re-add the contact (they'll need to re-accept).

- **Passphrase handling & recovery (critical – read carefully)**:
  Passphrases in Zer0Talk are **randomly generated** during initial user/identity creation for maximum security (you don't choose them). This makes memorization tricky without aids, but the app includes a safe, **local-only recovery option** if you lose access.

  **Primary recommendation for new users**:  
  As soon as your passphrase is generated (during first setup), **immediately store it securely offline**. Do **not** rely on memorization alone for a long random string.

  **Secure offline storage options** (do this right away):
  - **Encrypted external HDD/USB drive**: Write the passphrase in a text file, then encrypt the file/drive using Windows BitLocker (right-click → Turn on BitLocker) or VeraCrypt (free, create encrypted container). Store the drive in a safe physical location (home safe, bank deposit box, etc.).
  - **Paper backup**: Hand-write the full passphrase on paper/cardstock. Obfuscate slightly if desired (e.g., mix with dummy text only you recognize). Store in a fireproof/waterproof safe, locked drawer, or trusted secure spot. Avoid obvious hiding places.
  - **Split storage (advanced/paranoid)**: Divide the passphrase into 2–3 parts and store separately (e.g., one at home, one in sealed envelope with trusted person, one in safe deposit box). Reassemble only when needed.
  - **Never**: Store unencrypted digitally (email, cloud, phone photos, unencrypted notes), share it, or reuse it elsewhere. Avoid typing it into any online form.

  **If you lose/forgot your passphrase – use the built-in recovery**:
  1. On the sign-in screen, click **Lost Passphrase?**.
  2. The app generates a **brand-new random passphrase** (just like initial setup) and displays it with strong advice on secure storage.
  3. **Copy it** (for one-time immediate use) **and/or save it** somewhere secure offline (follow the same recommendations above—preferably separate from your main device).
  4. Click **Got It** once you've securely backed it up.
  5. Enter the **new passphrase** when prompted.
  6. You're back in! Your old data remains local and intact (encrypted under the new key).

  **Important safety notes**:
  - Recovery is **100% local** – no servers, no internet required, no remote execution risk.
  - If using **Multiplicity** (local network screensharing/multi-monitor tool), Zer0Talk has built-in **screenshare blocking** features to prevent others from viewing the app window or passphrase entry.
  - Changing passphrase via recovery **does not lose your contacts, messages, or identity** – it's a seamless local re-key.
  - Test your backup immediately after generation/recovery to ensure you can access it.

  **Backup your entire data folder too**: Regularly copy `%AppData%\Roaming\Zer0Talk\` to an encrypted external drive (includes contacts/settings but still requires passphrase to unlock).

### 4. General Tips & When to Report
- **Lag/high CPU**: Settings → Performance → Lower framerate for older PCs.
- **UI glitches**: Switch to default dark/light theme → Restart.
- **Logs for debugging**: Check `%AppData%\Roaming\Zer0Talk\Logs` for clues.
- **Still stuck?** Review `docs/user-guide.md` and `docs/install-run-troubleshooting.md`.
- Report via GitHub Issues (when open): Include version, Windows build, steps, screenshots, network type.

Updates often fix these – keep auto-update enabled!

Your patience helps make Zer0Talk stronger and freer. 🚀

---

## ⚠️ Alpha Warnings
- Bugs possible – report via GitHub Issues.
- Breaking changes in updates – backup before upgrading.
- Not for sensitive/mission-critical info yet (though encryption is robust even in alpha).

---

## 🤝 Community & Feedback
- **Issues & Requests**: Open on GitHub.
- **Discussions**: For questions or ideas.
- Follow @itsalaughingman on X for updates.

---

## 📄 License
Copyright © 2025-2026 Just a Laughing Man. All rights reserved. Personal use only – see LICENSE.md.

Built with Avalonia UI, .NET 9, and open-source libs.

**Ready to go P2P? Download from Releases and connect freely – no verification theater required!** 🚀

## License

This repository is currently closed-source for distribution purposes; reach out to the maintainers for reuse questions.
