# Zer0Talk — Privacy Policy

**Version:** 1.0  
**Effective Date:** March 10, 2026  
**Author:** AnotherLaughingMan

The authoritative copy of this document is maintained at:  
https://github.com/AnotherLaughingMan/Zer0Talk-Releases/blob/main/PRIVACY-POLICY.md

---

## Overview

Zer0Talk is a private, end-to-end encrypted peer-to-peer messaging application. This policy explains what data is handled by the application and how.

**Zer0Talk collects no data. There is no server that knows who you are, who you talk to, or what you say.**

---

## 1. Data Storage

All data is stored **locally on your device only**. Your account, contacts, messages, and settings are encrypted files stored in your local app data folder. No data is uploaded to any cloud service or third-party server.

Everything is encrypted at rest using **XChaCha20-Poly1305** with **Argon2id** key derivation — nothing is stored in plaintext.

---

## 2. Your Identity

Your identity is a keypair (Ed25519). Your unique ID is derived from your public key. No email address, phone number, or real name is required or registered with any authority. There is no central account registry. No one other than you holds your identity.

---

## 3. Messaging

All messages are **end-to-end encrypted**. Messages are encrypted before leaving your device and decrypted only on the recipient's device. Session keys are established via **ECDH (P-256)** — a shared secret known only to the two parties in a conversation. No message content, metadata, or delivery receipts are stored on any server.

---

## 4. Blind Relays

When a direct peer-to-peer connection is not possible (e.g., behind strict NAT or firewalls), Zer0Talk can route the connection through a **blind relay**. These are not servers in the traditional sense — they do not serve content, store data, or have any knowledge of what they are forwarding. A blind relay does one thing only: it facilitates the initial TCP connection request and then forwards an opaque, encrypted byte stream between two peers.

The relay sees only encrypted ciphertext and retains **nothing** — no logs, no session records, no metadata, no identity information. All cryptographic key exchange (ECDH) happens directly between the two clients through the relay-forwarded stream; the relay cannot participate in or observe the handshake. The relay is provably blind to all plaintext.

Blind relays can be self-hosted. Zer0Talk does not operate any relay infrastructure on your behalf.

---

## 5. No Telemetry

Zer0Talk does **not** collect analytics, crash reports, usage statistics, or telemetry of any kind. No data is sent to the developer, any analytics platform, or any third party.

Auto-update version checks query the GitHub Releases API to check for new versions. This request discloses your IP address to GitHub's servers and is subject to GitHub's privacy policy. No other identifying information is sent.

---

## 6. Features Intentionally Absent

The following features are **deliberately not implemented** due to privacy and safety considerations:

| Feature | Status | Reason |
|---|---|---|
| File / image transfers | Rejected | Exploitation and reputation risk |
| AI-assist features | Deferred | Privacy trust model not yet defined |
| Cloud message backup | Not planned | Contradicts the zero-knowledge principle |
| SMS / email bridging | Not planned | Requires a central account registry |
| Screen capture by third parties | Opt-out enabled by default | Windows Content Protection API |

---

## 7. Data Deletion

To delete your account securely, use **Settings → Danger Zone → Delete Account**. This process cryptographically wipes your keypair, identity, messages, and contacts. Manually deleting individual files (such as `user.p2e`) is **not** a secure deletion method — it may leave recoverable data on disk and bypass the multi-pass secure erase that the in-app deletion process performs.

Zer0Talk has no retention of your deleted data and cannot recover it once deleted. The developer has no access to your data and cannot fulfil data deletion requests on your behalf.

---

## 8. Legal Compliance

Zer0Talk operates without access to user data. As a result, the developer cannot comply with subpoenas, legal orders, or governmental requests for user data — there is nothing to produce.

Users are solely responsible for complying with all applicable local, national, and international laws when using Zer0Talk. Zer0Talk may not be used for surveillance, illegal activity, exploitation, or any purpose prohibited by applicable law.

---

## 9. Changes to This Policy

This policy may be updated in future versions of Zer0Talk. The authoritative and most current version is maintained at:  
https://github.com/AnotherLaughingMan/Zer0Talk-Releases/blob/main/PRIVACY-POLICY.md

Material changes will be surfaced via the in-app update notification system.

---

## 10. Contact

For questions about this policy, open an issue at:  
https://github.com/AnotherLaughingMan/Zer0Talk-Releases/issues

---

*This policy applies to the Zer0Talk application. It does not apply to third-party blind relays operated by users other than the developer.*
