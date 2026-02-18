# Zer0Talk Beginner Setup Guide (Client + Relay)

This guide is for first-time users who want to:
- run a Relay Server
- connect Zer0Talk clients through that relay
- optionally enable relay federation later

No advanced networking knowledge is required.

---

## 1) What You Are Setting Up

Zer0Talk has two pieces:

1. **Client App** (Zer0Talk)
   - what users run to chat

2. **Relay App** (Zer0Talk Relay)
   - helps users connect when direct peer-to-peer fails
   - keeps only short-lived routing data (ephemeral directory), not message history

### Decentralization and federation at a glance

- Zer0Talk remains decentralized even when relay fallback is enabled.
- Relays are connectivity helpers, not account/message authorities.
- Multiple relay operators can coexist (federated model); there is no required single central relay.
- Relays do not store message content and do not hold client private keys.

---

## 2) Quick Start (Same Network)

Use this first to confirm everything works.

### If both clients are on one PC and it still fails

Some home/router setups do not handle same-device network loops well.
If two clients on one machine cannot connect reliably, use two separate devices instead:
- Device A: relay + client A
- Device B: client B

### Step A: Start the Relay App

1. Launch Zer0Talk Relay.
2. In settings, keep these defaults for first test:
   - Port: `443` (or any free port)
   - DiscoveryEnabled: `true`
   - EnableFederation: `false`
3. Press Start in Relay UI.

### Step B: Point Clients to the Relay

On each Zer0Talk client:

1. Open Settings > Network.
2. Enable Relay Fallback.
3. Set Relay Server to:
   - `relay-machine-ip:port`
   - example: `192.168.1.50:443`
4. Save settings.

### Step C: Check that it works

- Add contact by UID on client A.
- Accept on client B.
- Send a test message.
- In Relay UI, you should see activity in Pending/Active and logs.

If this works, your base setup is good.

---

## 3) Internet Setup (Friends Outside Your LAN)

If users are outside your home network, do all of this on the relay host:

1. **Use a stable public endpoint**
   - domain name or static public IP

2. **Port forward on router**
   - forward external **TCP** `Port` to relay machine
   - example: external `443/TCP` -> internal `192.168.1.50:443/TCP`

2A. **Discovery port details (important)**
   - Discovery uses UDP `38384` on LAN only (multicast/broadcast).
   - Do **not** port-forward UDP `38384` to the internet.
   - Discovery is optional for internet users; direct `RelayServer host:port` config is enough.

3. **Allow firewall on relay machine**
   - allow inbound **TCP** for the relay `Port`
   - if using federation, allow inbound **TCP** for `FederationPort` too

4. **Set clients to public endpoint**
   - Relay Server = `your-domain-or-public-ip:port`

5. **Try from outside your home network**
   - test from mobile hotspot or a different internet connection

---

## 4) Federation Setup (Optional, Multi-Relay)

Use this only after single-relay setup works.

### Recommended starter values

- EnableFederation: `true`
- FederationPort: `8443`
- FederationTrustMode: `AllowList`
- FederationSharedSecret: strong shared value on all trusted relays
- PeerRelays: each peer relay using federation port

Example PeerRelays entry:
- `relay2.example.com:8443`

### Important behavior

- Main relay Port handles client commands.
- FederationPort handles relay-to-relay commands over **TCP** (for example `8443/TCP`).
- If federation is split-port mode, RELAY commands sent to main port are rejected.

### Firewall for federation

- Open main relay `Port/TCP` for clients.
- Open `FederationPort/TCP` for trusted relay peers.
- Prefer IP allowlisting for FederationPort.

---

## 5) Client Settings Cheatsheet

In Zer0Talk client network settings:

- RelayFallbackEnabled: `true`
- RelayServer: your main relay endpoint (host:port)
- SavedRelayServers: optional extra relay choices
- WanSeedNodes: optional bootstrap endpoints
- ForceSeedBootstrap: keep `false` unless troubleshooting bootstrap

---

## 6) How To Tell It Is Working

In Relay UI:

- **Pending > 0** means clients are trying to rendezvous.
- **Active > 0** means active relay sessions are established.
- Logs showing queued/paired session indicate live activity.

For federation:

- federation peer count logs should appear on startup
- bad shared-secret attempts should show unauthorized responses

---

## 7) Common Problems and Fixes

### Problem: Clients cannot connect at all

Check:
- relay app is running
- RelayServer value on clients is correct host:port
- firewall allows relay port
- router port-forward is correct (internet scenario)

### Problem: Works on LAN, fails on internet

Check:
- using private IP externally (wrong)
- no router port forward
- ISP blocks selected port

Try:
- different public port (for example 8444)
- update clients to new host:port

### Problem: Federation peers do not connect

Check:
- PeerRelays points to federation ports, not client ports
- shared secret matches exactly on all federated relays
- federation ports are open between relay hosts

---

## 8) Security Basics (Beginner Friendly)

- Use AllowList trust mode for federation.
- Use a strong FederationSharedSecret.
- Do not expose federation port to the whole internet if possible.
- Keep relay host OS updated.
- Back up relay config before major changes.

---

## 9) Recommended First Production Profile

Single relay (simple and safe):

- Port: `443`
- EnableFederation: `false` (start simple)
- DiscoveryEnabled: `true`
- RelayFallbackEnabled on clients: `true`

Then scale to federation after stable use.

---

## 10) If Same-Device Setup Fails

Use this simple fallback:

1. Keep relay running on machine A.
2. Configure both clients to use machine A relay address.
3. Put client B on another device/network path if possible.
4. Send a message from A to B and back.
5. If messages work both ways, your relay setup is good.

---

## Related Docs

- Full user documentation: `docs/user-guide.md`
- Relay config reference: `%APPDATA%\Zer0TalkRelay\relay-config-guide.md`
