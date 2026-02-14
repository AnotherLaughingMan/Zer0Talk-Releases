# Relay Server Setup

## Overview
The relay server is a separate Zer0Talk executable that forwards encrypted peer traffic and helps peers behind strict NAT connect. It does not store message content.

## Requirements
- Windows 11 Pro/Enterprise or Windows Server that supports .NET 9.
- Public IP or DNS name (IP-only is supported).
- TCP port 443 forwarded to the relay host (router + firewall).

## Build and Run (Local)
1. Build the solution:
   - `dotnet build .\Zer0Talk.sln`
2. Run the relay server:
   - `dotnet run --project .\Zer0Talk.RelayServer\Zer0Talk.RelayServer.csproj`

## Configuration
The relay server uses a JSON config stored in:
- `%APPDATA%\Zer0TalkRelay\relay-config.json`

Example:
```
{
  "Port": 443,
  "AutoStart": true,
  "MaxPending": 256,
  "MaxSessions": 512,
  "PendingTimeoutSeconds": 20,
  "BufferSize": 16384,
  "MaxConnectionsPerMinute": 120,
  "BanSeconds": 120
}
```

## Firewall and Port Forwarding
- Allow inbound TCP 443 on the relay host firewall.
- Forward TCP 443 on your router to the relay server machine.

## Client Setup (Manual)
Zer0Talk clients must be configured with the relay address:
- Settings field: Relay Server
- Format: `host:port` (example: `relay.example.com:443` or `203.0.113.10:443`)
- Enable relay fallback: `RelayFallbackEnabled = true`

The client will attempt direct/NAT connections first, then fall back to the relay.

## Client Auto-Discovery (DNS SRV)
DNS SRV discovery is planned but not yet implemented in the client. If you add SRV lookup, use:
- Service: `_zer0talk-relay._tcp.<your-domain>`
- Target: `relay.example.com`
- Port: `443`

Until SRV support lands, manual configuration is required.

## Service Mode (Optional)
Service auto-start is planned. For now, run the relay server as a standard user process.
