# Threat Model

This document describes the security goals, assumptions, and non-goals for Zer0Talk.

It is intentionally explicit about what the application is designed to protect, what it does not protect, and what environmental assumptions must hold for its guarantees to make sense.

This is not a formal proof, certification, or legal guarantee. It is the project's current engineering threat model for users, reviewers, auditors, and operators.

---

## 1. Scope

Zer0Talk is a Windows peer-to-peer messaging application with:

- pairwise end-to-end encrypted messaging
- local encrypted storage for user data
- direct connectivity when possible
- blind relay fallback when direct connectivity fails
- no central message server and no central account database

This threat model applies to the current Zer0Talk client and relay architecture as shipped in the repository.

---

## 2. Primary Security Goals

Zer0Talk is intended to provide the following properties under its stated assumptions:

1. Message confidentiality in transit between two honest endpoints.
2. Message confidentiality at rest on the local device when the app is locked and the local passphrase protections remain intact.
3. Session establishment without exposing plaintext to relay infrastructure.
4. Identity binding strong enough to detect obvious key-substitution and stale-contact mismatches.
5. Limited reduction of long-session exposure through in-session transport key rotation between compatible peers.
6. Minimal server-side trust by avoiding central message retention and central account custody.

---

## 3. Assets We Care About

The main assets Zer0Talk attempts to protect are:

- message plaintext
- local message history and contact data
- private identity key material
- session keys and transport key material
- integrity of peer identity binding
- user control over local-only stored data

---

## 4. Adversaries Considered

This model assumes Zer0Talk may face the following adversaries:

- passive network observers on the local network, ISP path, or public internet
- active network attackers attempting interception, replay, malformed input, or connection disruption
- untrusted or curious relay operators
- malicious or compromised peers sending hostile protocol input
- opportunistic local attackers without prior access to decrypted app state
- reviewers, auditors, or legal stakeholders who need the security claims stated narrowly and accurately

---

## 5. What Zer0Talk Is Claiming

Under normal operation, with uncompromised endpoints and current protocol assumptions, Zer0Talk is claiming the following:

- Message content is encrypted end to end between the two peer clients.
- Relay infrastructure does not decrypt message content and is not part of the key schedule.
- Fresh session establishment uses ephemeral ECDH-based transport setup.
- Transport key material is established per-session via ephemeral ECDH and refreshed on reconnect/new session establishment.
- Local application data is stored in encrypted containers rather than plaintext files.
- The application avoids a central service that stores message history for users.

These are engineering claims about the software design, not a promise of absolute secrecy in all environments.

---

## 6. What Zer0Talk Is Not Claiming

Zer0Talk does not claim any of the following:

- protection if the endpoint itself is compromised
- protection against malware, spyware, keyloggers, screen capture tools, or memory scrapers running on the local machine
- protection against a fully compromised Windows account, kernel, firmware, or hypervisor
- anonymity, unlinkability, or metadata-hiding comparable to Tor, mixnets, or specialized anonymity systems
- resistance to all traffic analysis, timing analysis, packet size correlation, or contact-pattern inference
- immunity to denial-of-service, packet dropping, throttling, relay blocking, or network partitioning
- immunity to social engineering, phishing, or a user trusting the wrong contact identity
- Signal-equivalent guarantees across all dimensions
- formal perfect forward secrecy or post-compromise security in the academic/protocol-proof sense for every deployment scenario
- protection for plaintext copied into the clipboard, shown on screen, or exposed through OS notifications
- protection once a user exports, backs up, screenshots, or otherwise handles data outside Zer0Talk's protected storage path
- secure deletion guarantees on SSD/NVMe media beyond best-effort handling already documented elsewhere
- safety against malicious third-party themes, plugins, tools, or unsupported runtime modifications

If any of the above properties are required, additional controls outside Zer0Talk are necessary.

---

## 7. Trust Assumptions

Zer0Talk's security claims depend on several assumptions:

1. The local endpoint is not already compromised.
2. The user keeps their passphrase and device reasonably secure.
3. The local operating system, cryptographic libraries, and runtime behave as expected.
4. The user verifies contacts appropriately when identity assurance matters.
5. The build being run corresponds to trusted source and dependencies.
6. Local backups, exported files, and copied data are handled securely by the user.

If these assumptions fail, Zer0Talk's protections may fail with them.

---

## 8. Relay and Metadata Limitations

Relays are designed as blind encrypted byte-forwarders, but they still expose some metadata.

A relay operator or network observer may still learn or infer:

- source and destination IP addresses visible to that relay hop
- approximate connection times and durations
- which clients contacted that relay
- coarse traffic volume and activity timing
- whether two peers appear to be communicating around the same time

Zer0Talk does not currently attempt to hide this metadata with cover traffic, onion routing, batching, or mix-network techniques.

---

## 9. Endpoint Compromise Limitation

This is the most important non-goal and should be read literally:

If an attacker controls the endpoint, Zer0Talk cannot meaningfully protect the confidentiality of messages displayed, typed, copied, decrypted, or stored in process memory on that endpoint.

Examples include:

- keyloggers capturing typed text or passphrases
- malware reading process memory
- screenshot or screen-recording software
- remote administration tools with user/session access
- clipboard capture or accessibility abuse
- malicious browser extensions or system overlays reading copied text

End-to-end encryption protects data in transit. It does not magically secure a compromised endpoint.

---

## 10. Side Channels and Advanced Analysis

Zer0Talk does not claim to eliminate advanced side channels.

Examples include:

- timing correlation between sent and received activity
- packet size and burst pattern analysis
- local hardware or cache side channels
- power-analysis or physical-lab extraction scenarios
- OS telemetry outside the app's control
- forensic recovery opportunities created by swap, hibernation, crash dumps, backups, or external tools

Some of these are partially mitigated by good OS hygiene, but they are not solved by Zer0Talk itself.

---

## 11. Forward Secrecy Statement

Zer0Talk establishes fresh transport state with ephemeral ECDH when a session is created.

The current release line does not claim in-session ratcheting. Forward-secrecy properties are tied to session re-establishment and key freshness per new connection.

Stated plainly:

- Zer0Talk does provide session freshness at handshake boundaries.
- Zer0Talk does not currently claim live-session ratchet rotation semantics.

---

## 12. Denial of Service

Availability is a secondary goal, not the primary one.

Attackers may still:

- refuse connections
- exhaust relay capacity
- flood or stall sockets
- block ports or relay hosts
- force fallback paths
- degrade latency or reliability

Zer0Talk attempts to bound and harden these cases, but does not claim continuous availability against determined network attackers.

---

## 13. Legal and Audit Reading Guide

For legal, compliance, or audit readers, the safest summary is:

- Zer0Talk is designed to reduce central trust and protect message content in transit and at rest.
- Zer0Talk does not claim to protect a compromised endpoint.
- Zer0Talk does not claim anonymity or metadata-hiding against network observers.
- Zer0Talk does not claim formal proof-level guarantees beyond the implemented design and reviewed code.
- Zer0Talk's claims are bounded by its operating assumptions, documented limitations, and current alpha status.

---

## 14. Related Documents

- `README.md`: user-facing product overview
- `SECURITY.md`: vulnerability reporting policy and high-level limitations
- `docs/PRIVACY-POLICY.md`: user-facing privacy disclosures
- `DEVELOPER-BIBLE.md`: implementation-oriented protocol and crypto notes

---

## 15. Status

This document reflects the current Zer0Talk architecture as of the latest repository state and should be updated whenever major security claims, transport protocol behavior, or storage assumptions change.
