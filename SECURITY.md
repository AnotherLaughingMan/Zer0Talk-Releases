# Security Policy

## Supported Versions

Zer0Talk is currently in **alpha**. Only the latest release receives security fixes.

| Version | Supported |
|---------|-----------|
| Latest alpha | ✅ |
| Older releases | ❌ |

---

## Security Architecture Summary

Zer0Talk is built with privacy and tamper-resistance as first-class requirements, not afterthoughts. Key properties:

- **End-to-end encrypted.** All message content is encrypted between peer clients. No relay or directory server ever holds or processes plaintext.
- **Zero server-side data.** Relay infrastructure is blind TCP forwarding only — no logging, no message storage, no metadata retention.
- **Ephemeral session keys.** Each session uses a fresh ECDH P-256 keypair. Compromise of one session does not affect others.
- **Identity-bound sessions.** UID verification after ECDH handshake prevents MITM substitution. Mismatch triggers a security alert and immediate disconnect.
- **Offline-first local storage.** All user data lives in `%AppData%\Roaming\Zer0Talk\` in P2E3-encrypted containers (XChaCha20-Poly1305 + Argon2id KDF). No data leaves the device except what you choose to send peer-to-peer.
- **No attachments or file transfers.** By design and policy. Reduces attack surface and eliminates entire classes of exploitation risk.

For the full cryptographic and protocol specification, see [DEVELOPER-BIBLE.md](DEVELOPER-BIBLE.md) Section 5.

---

## Reporting a Vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Report vulnerabilities privately via GitHub's built-in security advisory system:

1. Go to the [Security tab](https://github.com/AnotherLaughingMan/Zer0Talk/security) on the GitHub repository.
2. Click **"Report a vulnerability"**.
3. Fill in a clear description with reproduction steps.

You can also reach the project owner directly through GitHub.

### What to Include

A good report includes:

- A clear description of the vulnerability and its potential impact.
- Steps to reproduce, including any relevant configuration or environment details.
- Proof-of-concept code or screenshots where applicable.
- Your assessment of severity (and why).

Vague reports without reproduction steps may not receive timely responses.

### Response Timeline

| Stage | Target |
|-------|--------|
| Acknowledgment | Within 7 days |
| Triage and severity assessment | Within 14 days |
| Fix or mitigation | Depends on severity and complexity |
| Public disclosure | After fix is released, coordinated with reporter |

This is a solo project maintained by one person. Response times reflect that reality — not lack of interest.

---

## Scope

### In Scope

- Vulnerabilities in Zer0Talk's cryptographic protocol (ECDH, HKDF, AeadTransport, P2E3 format).
- Memory disclosure or key leakage bugs.
- Replay or MITM attacks that bypass identity binding.
- Remote code execution via crafted peer messages or relay data.
- Authentication/identity bypasses.
- Denial-of-service attacks against the relay server that affect availability.
- Insecure handling of inbound payload data (buffer overflows, format string issues, etc.).

### Out of Scope

- Vulnerabilities in third-party dependencies (report to the upstream maintainer; we will update our dependency).
- Social engineering or phishing of end users.
- Physical access attacks against a user's machine.
- Attacks that require the attacker to already have control of the target's OS (Admin / SYSTEM privilege escalation is the OS's problem, not ours).
- Issues requiring a user to deliberately configure an insecure environment.
- "Security theater" reports (e.g., missing HTTP security headers on a TCP-only app, absence of 2FA on a zero-account app).

---

## Known Limitations

These are documented limitations, not vulnerabilities:

- **Alpha software.** The protocol and storage format may change between releases. Do not rely on this software for life-critical communications yet.
- **Windows-only.** Security properties are only verified on Windows 10/11 x64. Other platforms are not supported.
- **SSD / NVMe secure deletion.** Secure wipe on SSDs is not effective due to wear-leveling. The app detects drive type and skips overwrite passes on SSD/NVMe, but underlying data may persist in NAND cells until overwritten naturally by the OS or drive firmware.
- **Relay trust.** Using a third-party relay means trusting the relay operator not to perform traffic analysis on connection metadata (who connects to whom, and when). The relay cannot read message content. Self-hosting the relay eliminates this trust requirement.
- **Local OS trust boundary.** Zer0Talk cannot protect against malware already running with user-level or higher privilege on the same machine.

---

## Disclosure Policy

- We practice **coordinated disclosure**. We ask reporters to give us a reasonable window to fix before going public.
- We will credit reporters in the release notes for responsibly disclosed vulnerabilities, unless anonymity is requested.
- We do not offer bug bounties at this time.

---

## AI Tooling Disclosure

Consistent with our development transparency policy, security-relevant code (cryptographic primitives, transport protocol, payload validation) is reviewed and directed by the project owner. AI agents assist with implementation but do not autonomously make security architecture decisions. See [DEVELOPER-BIBLE.md §21](DEVELOPER-BIBLE.md#21-ai-in-the-development-workflow) for full disclosure.
