# IP Blocking System Documentation

## Overview
Zer0Talk includes multiple blocking layers. The primary controls are peer/identity and public-key fingerprint blocking. IP blocking is an optional, narrower layer for unwanted connections.

## Current Behavior
- Blocks connections to IPs listed in user settings.
- Stores IPs and CIDR ranges in encrypted settings, but range/geo enforcement is limited in current builds.
- A default IP blocklist resource exists, but it is not auto-loaded unless explicitly wired in your build.

## Configuration
Access IP blocking settings through: **Settings → Network → Bad-Actor IP Blocking** (if enabled)

### Manual Entry
- Add individual IPs: `203.0.113.1`
- Add CIDR ranges: `192.168.1.0/24` (stored for future enforcement)

## Storage
- Block lists are persisted in the encrypted settings container.
- The remembered-passphrase sidecar is protected with Windows DPAPI.

## Notes
- IP blocking complements, but does not replace, peer/identity and key-fingerprint blocking.
- If you need strict IP range or geo-blocking, verify your build wires the default list loader and range checks.