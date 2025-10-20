# ZTalk User Guide

This user guide provides step-by-step instructions for common tasks in ZTalk: launching the app, creating an identity, adding and deleting contacts, running a dedicated node, using the installer, and basic troubleshooting.

## Table of Contents

- Getting Started
- Creating an Identity
- Adding Contacts
- Deleting Contacts
- Sending Messages
- Message Options (Edit / Delete / Burn)
- Themes & Appearance
- Notifications & Sounds
- Running a Dedicated Node (Advanced)
- Backup and Restore
- Troubleshooting

## Getting Started

1. Install ZTalk using the installer or extract the standalone zip.
2. Launch `ZTalk.exe` from the Start Menu or extracted folder.
3. On first run, follow the Initial Setup wizard to create your identity.

## Creating an Identity

When launching ZTalk for the first time you will be prompted to create your identity.

- Display Name: The name shown to your contacts.
- Passphrase (optional): A local passphrase used to protect certain settings and enable identity recovery.

Tips:
- Use a memorable passphrase if you expect to move settings across devices.
- If you skip the passphrase, your identity will still be generated but may be harder to recover.

## Adding Contacts

There are two ways to add a contact:

### 1) Send/Receive Contact Request
- Open the "Add Contact" dialog from the Contacts or File menu.
- Enter your contact's ZTalk ID (a short peer ID or QR code) and an optional message.
- Send the request. Your contact will receive the request and can accept to add you back.

### 2) Receive an Invitation Link or QR Code
- If someone shares a ZTalk invitation (link or QR), click "Import Invitation" in the Add Contact dialog and paste the link or scan the QR code.
- Confirm the import and optionally provide a local alias.

Notes:
- Contact requests are mutual: the remote user must accept your request before they're added to your list.
- Verification badges appear when a contact shares a verified identity or when you've manually verified them.

## Deleting Contacts

To delete a contact from your list:
1. Open the contact's profile or right-click the contact in the contact list.
2. Choose "Remove Contact".
3. You will be asked whether to also erase local message history for this contact.
4. Confirm. The contact will be removed locally. If the other party still has you, they will not be notified automatically.

Notes on data removal:
- If you choose to erase message history, ZTalk will attempt a secure wipe (overwrites local storage). Some filesystem-level remnants may persist depending on OS and filesystem.
- Deleting a contact does not automatically revoke any shared verification tokens on the remote side.

## Sending Messages

- Select a contact and type your message into the composer box.
- Press Enter to send, or Ctrl+Enter to insert a newline.
- Attach files using the attachment icon. Attachments are transferred peer-to-peer.

## Message Options

- Edit: Right-click a sent message and choose "Edit". Edited messages are propagated to recipients.
- Delete: Right-click and choose "Delete" to remove locally and send a deletion request to the peer.
- Burn (Ephemeral): Use the "Burn" option to set a lifetime for a message; once expired it will be removed from local storage and the peer will be requested to remove it as well.

## Themes & Appearance

Open Settings -> Themes to use the Theme Editor:
- Pick a base theme (Light/Dark)
- Adjust colors, gradients, and export/import theme files
- Save themes for sharing

## Notifications & Sounds

- Toggle sound and notification preferences in Settings -> Notifications.
- DND mode silences notifications and can be scheduled.
- System tray integrates for quick access and show/hide behavior.

## Running a Dedicated Node (Advanced)

A Dedicated Node is a longer-running instance of ZTalk configured to stay online so it can act as a relay or availability point for your contacts. This is optional and intended for advanced users.

### Intended Use Cases
- Improve message delivery when your client is offline
- Provide a stable public endpoint for NAT traversal and rendezvous

### Requirements
- A Windows server or VM reachable by peers (public IP recommended)
- A router/firewall allowing inbound TCP/UDP on the configured ports
- Sufficient disk space and a stable internet connection

### Quick Setup
1. Download the self-contained Release or Release-sc build and extract it on the server.
2. Open `settings.json` (or use the GUI Settings) and enable "DedicatedNode" or "RunAsNode" mode.
3. Configure the public endpoint and port forwarding.
4. Optionally configure identity recovery/passphrase for this node.
5. Start ZTalk on the server. The node will register with its peers via the peer discovery mechanism.

### Security Considerations
- Keep the node’s identity passphrase secure. Compromise of the node's keys can reveal your presence on the network.
- Consider using a reverse proxy and TLS if exposing administrative endpoints.

## Backup and Restore

- Export settings and identity via Settings -> Backup.
- Backup files are encrypted if you set a passphrase.
- Use the Restore option to import settings and identity on another device.

## Troubleshooting

- No connection: Ensure at least one node or peer is online, and check firewall/router settings.
- Messages not delivering: Check peer availability and logs in the app's diagnostics panel.
- Audio not playing: Confirm sound files exist in `Assets/Sounds` and system volume settings.

---

If you want, I can expand any section into step-by-step screenshots or a short quick-start video script.