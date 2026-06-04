# Security Policy

## Supported Versions

Zer0Talk is currently in alpha. Only the latest release receives security fixes.

| Version | Supported |
|---------|-----------|
| Latest alpha | ✅ |
| Older releases | ❌ |

---

## Security Architecture Summary

Zer0Talk is designed around privacy and tamper resistance from the start. Key properties:

- End-to-end encrypted. Message content is encrypted between peer clients. No relay or directory server processes plaintext.
- Zero server-side data. Relay infrastructure is blind TCP forwarding only, with no message storage.
- Ephemeral session keys. Each session uses a fresh ECDH P-256 keypair.
- Identity-bound sessions. UID verification after ECDH helps prevent MITM substitution.
- Offline-first local storage. User data lives in `%AppData%\Roaming\Zer0Talk\` in P2E3 encrypted containers (XChaCha20-Poly1305 + Argon2id KDF).
- No attachments or file transfers. This is intentional policy to reduce attack surface.

For the full cryptographic and protocol specification, see [DEVELOPER-BIBLE.md](DEVELOPER-BIBLE.md) Section 5.
For explicit assumptions, non-goals, and threat boundaries, see [THREAT-MODEL.md](THREAT-MODEL.md).

---

## Reporting a Vulnerability

Do not open a public GitHub issue for security vulnerabilities.

Report vulnerabilities privately via GitHub's built-in security advisory system:

1. Go to the [Security tab](https://github.com/AnotherLaughingMan/Zer0Talk/security) on the GitHub repository.
2. Click Report a vulnerability.
3. Include a clear description and reproduction steps.

You can also contact the project owner directly through GitHub.

### What to Include

A good report includes:

- A clear description of the vulnerability and potential impact.
- Reproduction steps, including relevant configuration or environment details.
- Proof-of-concept code or screenshots where applicable.
- Your severity assessment and rationale.

Reports without clear reproduction steps may take longer to process.

### Response Timeline

| Stage | Target |
|-------|--------|
| Acknowledgment | Within 7 days |
| Triage and severity assessment | Within 14 days |
| Fix or mitigation | Depends on severity and complexity |
| Public disclosure | After fix is released, coordinated with reporter |

This is a solo-maintained project, so response times reflect available bandwidth.

---

## Scope

### In Scope

- Vulnerabilities in Zer0Talk's cryptographic protocol (ECDH, HKDF, AeadTransport, P2E3 format).
- Memory disclosure or key leakage bugs.
- Replay or MITM attacks that bypass identity binding.
- Remote code execution via crafted peer messages or relay data.
- Authentication or identity bypasses.
- Denial-of-service attacks against the relay server affecting availability.
- Insecure handling of inbound payload data.

### Out of Scope

- Vulnerabilities in third-party dependencies (report upstream; we will update dependencies as fixes land).
- Social engineering or phishing of end users.
- Physical access attacks against a user's machine.
- Attacks that require the attacker to already control the target OS.
- Issues that require users to deliberately configure an insecure environment.
- Low-value "security theater" reports (for example HTTP header checks against a TCP app, or 2FA requests for a zero-account app).

---

## Known Limitations

These are documented limitations, not vulnerabilities:

- Alpha software. Protocol and storage format may change between releases.
- Windows-only. Security properties are only verified on Windows 10/11 x64.
- SSD/NVMe secure deletion. Overwrite wipes are not reliable on SSDs due to wear-leveling.
- Relay trust. Third-party relays can observe traffic metadata (who connects and when), but cannot read message content.
- Local OS trust boundary. Zer0Talk cannot protect against malware already running on the same machine.

See [THREAT-MODEL.md](THREAT-MODEL.md) for the full statement of non-claims and operating assumptions.

---

## Disclosure Policy

- We practice coordinated disclosure and ask reporters to allow time for a fix.
- We credit reporters in release notes unless anonymity is requested.
- We do not offer bug bounties at this time.

---

## AI Tooling Disclosure

Security-sensitive code (cryptography, transport protocol, payload validation) is reviewed and directed by the project owner. AI tools assist implementation but do not independently set security architecture policy. See [DEVELOPER-BIBLE.md §21](DEVELOPER-BIBLE.md#21-ai-in-the-development-workflow) for full disclosure.
