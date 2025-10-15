# IP Blocking System Documentation

## Overview
ZTalk includes a comprehensive IP blocking system to protect against bad-actor IPs and hostile network infrastructure.

## Features
- **Individual IP Blocking**: Block specific IP addresses
- **CIDR Range Blocking**: Block entire network ranges (e.g., `192.168.1.0/24`)
- **Default Blocklist**: Ships with ~27,000 IP ranges from high-risk countries
- **Import/Export**: Import threat intelligence feeds and backup configurations
- **Real-time Validation**: IP address and CIDR format validation
- **Persistent Storage**: Encrypted storage of custom block lists

## Default Blocklist
The application ships with a comprehensive default blocklist containing:
- **China**: 3,953 IP ranges
- **Russia**: 6,361 IP ranges  
- **India**: 16,870 IP ranges
- **North Korea**: 4 IP ranges

**Total**: ~27,000 IP ranges for enhanced security

## Configuration
Access IP blocking settings through: **Settings → Network → Bad-Actor IP Blocking**

### Manual Entry
- Add individual IPs: `203.0.113.1`
- Add CIDR ranges: `192.168.1.0/24`
- Real-time validation and feedback

### Import Lists
Place IP block list files in:
- `%APPDATA%\ZTalk\security\ip-blocklist.txt` (recommended)
- Desktop (`ip-blocklist.txt`)
- Downloads folder (`ip-blocklist.txt`)

### Supported Sources
- **Spamhaus**: DROP/EDROP lists
- **FireHOL**: Community IP lists  
- **abuse.ch**: Malware blocklists
- **Commercial providers**: CrowdStrike, Recorded Future, etc.

### Export/Backup
- Export current lists with timestamps
- Automatic backup to `%APPDATA%\ZTalk\security\`
- Includes both individual IPs and CIDR ranges

## File Format
```
# Comments start with # or //
# One IP address or CIDR range per line

# Individual IPs
203.0.113.1      # Example botnet C&C
198.51.100.5     # Example malware host

# CIDR ranges  
203.0.113.0/24   # Example compromised network
192.168.1.0/24   # Block entire subnet
```

## Integration
- Automatically initializes default blocklist on startup
- Integrates with existing SecurityBlocklistService
- Persistent storage via encrypted P2E container
- Real-time UI updates in Network settings

## Implementation Details
- **Service**: `IpBlockingService.cs`
- **UI**: Settings → Network panel  
- **Storage**: Encrypted via `AppSettings.cs`
- **Default Resource**: `Assets/Security/default-ip-blocklist.txt`
- **Initialization**: Auto-loads on app startup after unlock

## Security Considerations
- All custom IP lists stored encrypted
- Default blocklist embedded as application resource
- No automatic updates to prevent vendor lock-in
- User-controlled threat intelligence approach
- Proper Windows application data structure (`%APPDATA%\ZTalk\security\`)