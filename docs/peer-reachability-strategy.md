# Peer Reachability Strategy (Security-First)

## Goals
- Improve successful peer connections outside LAN.
- Preserve end-to-end encryption and identity guarantees.
- Avoid introducing trust in relays for message content.

## Connection Path Order
1. LAN direct (existing multicast/broadcast discovery + direct TCP).
2. Direct WAN connect using known endpoint.
3. NAT-assisted UDP hole punch.
4. Relay fallback (E2EE over relay tunnel).
5. Deferred retry with exponential backoff.

## Security Constraints (Non-Negotiable)
- Keep session identity binding strict (derived UID must match expected peer UID).
- Keep relay blind to plaintext (E2EE transport only).
- Never auto-trust endpoint changes without cryptographic verification.
- Enforce per-peer and global attempt throttles to reduce abuse surface.

## Recommended Improvements
### 1) Multi-relay fallback pool
- Support multiple configured relay endpoints.
- Health-score each relay by success/latency/error rate.
- Fail over quickly on connection timeout.

### 2) Candidate endpoint cache
- Store last successful endpoint/path per peer with TTL.
- Prefer successful route first on reconnect.
- Invalidate immediately on identity mismatch.

### 3) Reachability telemetry
- Track counters: direct success, punch success, relay success, timeout, mismatch.
- Surface summary in Monitoring window for tuning.

### 4) Adaptive retry policy
- Keep bounded in-flight connect attempts.
- Apply jittered backoff per peer.
- Retry less aggressively during repeated failures.

### 5) Hairpin handling policy
- Treat hairpin check as diagnostic/advisory, not hard connectivity failure.
- Use direct/punch/relay outcomes as primary success signal.

## Phased Rollout
### Phase A (low risk)
- Multi-relay list + health scoring.
- Endpoint cache with TTL.
- Monitoring counters.

### Phase B
- Adaptive retry tuning from telemetry.
- Smarter fallback ordering by historical success.

### Phase C
- Optional regional relay preference with privacy-preserving selection.

## Success Metrics
- Higher percentage of successful peer session establishment outside LAN.
- Lower median time-to-connected.
- Fewer repeated connect attempts during outage periods.
- No increase in identity-mismatch or security-warning events.
