# Contact Request Bidirectional Fix

**Date:** October 6, 2025  
**Issue:** Contact requests were not bidirectional - accepting a request didn't automatically add both parties to each other's contact lists.

## Problems Fixed

### 1. Non-Bidirectional Contact Addition
**Before:** 
- User A sends contact request to User B
- User B accepts
- User B gets added to User A's contact list
- User A does NOT get added to User B's contact list
- User B had to send a separate request back to User A

**After:**
- User A sends contact request to User B
- User B accepts
- **Both** User A and User B are automatically added to each other's contact lists
- No additional request needed

### 2. Offline Contact Requests
**Before:**
- If recipient was offline, contact request would fail
- No retry mechanism

**After:**
- Contact requests to offline peers are queued with timestamp
- When peer comes online (detected via presence), queued request is automatically retried
- User gets notified in logs about queuing and retry attempts

## Technical Changes

### NetworkService.cs

#### Modified `SendContactAcceptAsync` Method
- **Old Protocol:** 0xC1 frame only contained nonce
  ```
  [0xC1][nonce_len][nonce]
  ```
- **New Protocol:** 0xC1 frame now includes accepter's display name
  ```
  [0xC1][nonce_len][nonce][dn_len][display_name]
  ```

#### Updated Inbound 0xC1 Frame Handler (2 locations)
- Added parsing logic to extract display name from enhanced 0xC1 frames
- Backward compatible - handles old frames without display name
- Passes accepter UID and display name to `ContactRequestsService.OnInboundAccept`

### ContactRequestsService.cs

#### Enhanced `OnInboundAccept` Method
**New Signature:**
```csharp
public void OnInboundAccept(string nonce, string accepterUid, string? accepterDisplayName)
```

**Functionality:**
1. Automatically creates contact entry for the accepter
2. Checks for duplicate contacts before adding
3. Persists contact with encryption using DPAPI
4. Triggers immediate peer verification if peer is online
5. Updates peer manager to refresh UI
6. Logs bidirectional add action
7. Completes TaskCompletionSource for original requester

#### Added Offline Request Queue
**New Fields:**
```csharp
private readonly ConcurrentDictionary<string, (string Nonce, string DisplayName, DateTime QueuedAt)> _offlineQueue
```

**New Methods:**
- `RetryOfflineRequestAsync(string uid)` - Retries a queued request
- `OnPeerOnline(string uid)` - Called when peer comes online to trigger retry

#### Modified `SendRequestAsync` Method
- Queues failed connection attempts to offline queue
- Logs queuing action for user visibility

### AppServices.cs

#### Enhanced PresenceReceived Handler
- Added call to `ContactRequests.OnPeerOnline(uid)` when presence is received
- Ensures offline contact request retry happens automatically
- Integrated with existing presence-based connection logic

## Protocol Compatibility

### Backward Compatibility
The implementation is **backward compatible**:
- New clients sending 0xC1 frames include display name
- New clients receiving 0xC1 frames check for display name field
- If display name is missing (old protocol), gracefully degrades
- Old clients will ignore extra bytes in frame (standard frame parsing behavior)

### Forward Compatibility
- Frame structure allows future extensions
- Display name field is optional and nullable
- Additional fields can be added after display name in future versions

## Behavior Flow Examples

### Scenario 1: Both Online, Fresh Request
1. Alice sends contact request to Bob (includes "Alice Smith")
2. Bob receives request notification
3. Bob clicks "Accept"
4. **0xC1 frame sent:** `[0xC1][nonce][7]["Bob Johnson"]`
5. Alice's client receives 0xC1
6. Alice's contact list gains "Bob Johnson"
7. Bob's contact list gains "Alice Smith"
8. Both see each other with verified badge if keys match

### Scenario 2: Recipient Offline
1. Alice sends contact request to Carol (offline)
2. Connection fails - request queued
3. Log entry: "Queued offline contact request for usr-carol123"
4. Carol comes online, sends presence
5. Alice's client detects presence
6. Automatic retry: "Retrying offline contact request for usr-carol123"
7. Carol receives request notification
8. (Process continues as Scenario 1)

### Scenario 3: Recipient Online, Accepter Offline at Acceptance
1. Dave sends request to Eve (online)
2. Eve accepts immediately
3. 0xC1 frame sent with Eve's display name
4. Dave is offline - frame queued by network layer
5. Dave comes online
6. Frame delivered, Dave's contact list updated
7. Bidirectional add complete

## Testing Recommendations

### Manual Tests
1. **Basic Bidirectional:** 
   - Two clients on LAN
   - Send request, accept
   - Verify both contact lists updated

2. **Offline Queue:**
   - Start Client A
   - Keep Client B offline
   - Client A sends request to B
   - Start Client B
   - Verify request appears in B's notifications

3. **Display Name Propagation:**
   - Client A has display name "John"
   - Client B has display name "Jane"
   - Send request both directions
   - Verify correct display names appear in both contact lists

4. **Duplicate Prevention:**
   - Add contact manually
   - Accept request from same contact
   - Verify no duplicate entries

### Automated Test Cases
- [ ] Bidirectional add on first request/accept
- [ ] Offline queue persistence across app restarts
- [ ] Display name extraction from 0xC1 frames
- [ ] Backward compatibility with old 0xC1 format
- [ ] Duplicate contact detection
- [ ] Peer verification after auto-add

## Known Limitations

1. **Offline Queue Persistence:** Currently in-memory only
   - Queued requests lost on app restart
   - Future: Persist to encrypted settings

2. **Queue Timeout:** No automatic expiration
   - Offline requests remain queued indefinitely
   - Future: Add 7-day TTL and cleanup

3. **Display Name Updates:** 
   - Uses display name at time of accept
   - Won't auto-update if sender changes name later
   - Requires manual contact edit

## Logs to Monitor

- `logs/zer0talk-YYYYMMDD-HHMMSS.log`:
  - "Auto-added {name} to contacts after they accepted our request"
  - "Queued offline contact request for {uid}"
  - "Retrying offline contact request for {uid}"

- `logs/network.log`:
  - `send C1 contact-accept | peer={uid} | nonce={nonce} | dnLen={len}`
  - `recv C1 contact-accept | peer={uid} | nonce={nonce} | dnLen={len}`

## Migration Notes

**No database migration required** - this is a protocol and logic enhancement only.

**Settings Impact:** None - uses existing contact storage encryption.

**Network Impact:** Slightly larger 0xC1 frames (typically +10-50 bytes for display name).

## Future Enhancements

1. **Persistent Offline Queue:** Store in encrypted settings
2. **Queue TTL:** Auto-expire old requests (suggested: 7 days)
3. **Notification Improvements:** Toast on bidirectional add success
4. **Batch Request Handling:** Support multiple pending requests per peer
5. **Request Metadata:** Include avatar hash, public key in request frame
