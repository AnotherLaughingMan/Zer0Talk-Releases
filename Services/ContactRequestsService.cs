using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Models = ZTalk.Models;

namespace P2PTalk.Services
{
    public enum ContactRequestResult
    {
        NotFound,
        Accepted,
        Rejected,
        Timeout,
        Failed
    }

    public class ContactRequestsService
    {
        private readonly IdentityService _identity;
        private readonly NetworkService _net;
        private readonly SettingsService _settings;
        private readonly ContactManager _contacts;
        private readonly DialogService _dialogs;
    // Pending inbound requests (nonce keyed) for UI panel / toast
    private readonly ConcurrentDictionary<string, PendingContactRequest> _pendingInbound = new();
    // Map UID -> latest nonce (helps with de-dupe)
    private readonly ConcurrentDictionary<string, string> _uidToNonce = new(StringComparer.OrdinalIgnoreCase);
    // Verification state (ephemeral, session-based). When both sides indicate intent, mark verified.
    private readonly ConcurrentDictionary<string, bool> _verifyInitiated = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _verifyReceived = new(StringComparer.OrdinalIgnoreCase);
    // Manual verification requests (peer asks to open dialog). Track pending by UID.
    private readonly ConcurrentDictionary<string, bool> _verifyRequestPending = new(StringComparer.OrdinalIgnoreCase);

        public ContactRequestsService(IdentityService identity, NetworkService net, SettingsService settings, ContactManager contacts, DialogService dialogs)
        {
            _identity = identity; _net = net; _settings = settings; _contacts = contacts; _dialogs = dialogs;
        }

        // nonce -> completion
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ContactRequestResult>> _pending = new();

    // UI event raised on dispatcher by caller: a new inbound contact request enqueued
    public event Action<PendingContactRequest>? RequestReceived;
    // Raised when a manual verification request arrives (show in notifications)
    public event Action<string>? VerifyRequestReceived; // arg: uid
    public event Action<string>? VerifyRequestCancelled; // arg: uid

    public IReadOnlyCollection<PendingContactRequest> PendingInboundRequests => _pendingInbound.Values.ToArray();

        // [VERIFY] expectedPublicKeyHex: optional hex public key provided by user to validate identity. Lowercase, no separators.
        // Simple per-UID throttle to avoid spamming the same peer with requests due to UI retries or flaky sessions.
        private readonly ConcurrentDictionary<string, DateTime> _lastOutboundAt = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan OutboundMinInterval = TimeSpan.FromSeconds(5);

        public async Task<ContactRequestResult> SendRequestAsync(string uid, string? host = null, int? port = null, TimeSpan? timeout = null, string? expectedPublicKeyHex = null)
        {
            timeout ??= TimeSpan.FromSeconds(20);
            uid = Trim(uid);
            // Throttle repeated outbound requests to the same UID
            var now = DateTime.UtcNow;
            if (_lastOutboundAt.TryGetValue(uid, out var last) && (now - last) < OutboundMinInterval)
            {
                SafeLogNetError($"contact-request throttled uid={uid} since={(now-last).TotalMilliseconds:F0}ms<min={OutboundMinInterval.TotalMilliseconds:F0}ms");
                return ContactRequestResult.Failed;
            }
            _lastOutboundAt[uid] = now;
            // Resolve endpoint either from parameters or from discovered peers
            if (host == null || port == null)
            {
                foreach (var p in AppServices.Peers.Peers)
                {
                    if (string.Equals(uid, Trim(p.UID), StringComparison.OrdinalIgnoreCase))
                    { host = p.Address; port = p.Port; break; }
                }
            }
            if (host == null || port == null || string.IsNullOrWhiteSpace(host) || port == 0) return ContactRequestResult.NotFound;

            using var cts = new CancellationTokenSource(timeout.Value);
            // Step 1: ensure connection + encrypted session
            var connectStart = DateTime.UtcNow;
            var okRelayOrDirect = await _net.ConnectWithRelayFallbackAsync(Trim(uid), host!, port!.Value, cts.Token);
            if (!okRelayOrDirect)
            {
                SafeLogNetError($"contact-request connect-fail uid={Trim(uid)} host={host} port={port}");
                return ContactRequestResult.NotFound;
            }
            // Wait a short grace period for session registration (handshake completion)
            var sessionReady = await _net.WaitForEncryptedSessionAsync(Trim(uid), TimeSpan.FromSeconds(6), cts.Token);
            if (!sessionReady)
            {
                SafeLogNetError($"contact-request no-session uid={Trim(uid)} after-connect elapsed={(DateTime.UtcNow-connectStart).TotalMilliseconds:F0}ms");
                return ContactRequestResult.Failed;
            }

            var nonce = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<ContactRequestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[nonce] = tcs;
            try
            {
                // Step 2: attempt framed send with small retry loop (transient session races)
                var displayName = _identity.DisplayName ?? string.Empty;
                var attempt = 0; bool sent = false;
                while (!sent && attempt < 3 && !cts.Token.IsCancellationRequested)
                {
                    attempt++;
                    try
                    {
                        sent = await _net.SendContactRequestAsync(uid, nonce, displayName, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        SafeLogNetError($"contact-request send-ex uid={uid} attempt={attempt} ex={ex.GetType().Name}:{ex.Message}");
                    }
                    if (!sent)
                    {
                        await Task.Delay(150 * attempt, cts.Token); // linear backoff
                    }
                }
                if (!sent)
                {
                    SafeLogNetError($"contact-request send-fail uid={uid} attempts={attempt}");
                    return ContactRequestResult.Failed;
                }
                // Await accept/cancel
                using var _ = cts.Token.Register(() => tcs.TrySetResult(ContactRequestResult.Timeout));
                var result = await tcs.Task;
                if (result == ContactRequestResult.Accepted)
                {
                    // Add contact locally. We do NOT use our own display name here.
                    // If we don't have a peer-provided name, default to UID; it can be updated later.
                    var dn = uid;
                    var c = new Models.Contact { UID = uid, DisplayName = dn, ExpectedPublicKeyHex = NormalizeHex(expectedPublicKeyHex) };
                    _contacts.AddContact(c, AppServices.Passphrase);
                    AppServices.Peers.IncludeContacts();
                    // If the peer is currently known and has an observed pubkey, validate immediately
                    TryImmediatePeerVerification(c);
                }
                return result;
            }
            finally
            {
                _pending.TryRemove(nonce, out _);
                // Connection/session lifecycle is managed by NetworkService (_sessions); nothing to close here.
            }
        }

        // [VERIFY] If the peer is present and has PublicKey, compare vs ExpectedPublicKeyHex and update flags.
        private void TryImmediatePeerVerification(Models.Contact contact)
        {
            try
            {
                var peer = AppServices.Peers.Peers.Find(p => string.Equals(p.UID, contact.UID, StringComparison.OrdinalIgnoreCase));
                if (peer?.PublicKey is { Length: > 0 })
                {
                    var observed = Convert.ToHexStringLower(peer.PublicKey);
                    var expected = NormalizeHex(contact.ExpectedPublicKeyHex);
                    var match = !string.IsNullOrWhiteSpace(expected) && string.Equals(observed, expected, StringComparison.Ordinal);
                    contact.PublicKeyVerified = match;
                    _contacts.SetPublicKeyVerified(contact.UID, match);
                    if (match)
                    {
                        // Persist verification so it survives reconnects and app restarts
                        _contacts.SetIsVerified(contact.UID, true, AppServices.Passphrase);
                    }
                    // Reflect into peer for unified UI (Peers list) and notify change
                    peer.PublicKeyVerified = match;
                    if (!match && !string.IsNullOrWhiteSpace(expected))
                    {
                        SafeLogNetError($"Public key mismatch for {contact.UID}: expected {expected}, observed {observed}");
                    }
                    // Update peer manager state to ensure UI refresh
                    try { AppServices.Peers.SetPeerVerification(contact.UID, match); } catch { }
                }
            }
            catch { }
        }

        private static string? NormalizeHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            var s = hex.Trim().ToLowerInvariant();
            return s.Replace("-", string.Empty).Replace(":", string.Empty).Replace(" ", string.Empty);
        }

    // Immutable pending request description exposed to UI
    public readonly record struct PendingContactRequest(string Uid, string Nonce, string DisplayName, DateTime ReceivedAt);

    // [VERIFY] Scoped logging to network.log for mismatches only
        private static void SafeLogNetError(string line)
        {
            try
            {
                    if (!Utilities.LoggingPaths.Enabled) return;
                    var path = Utilities.LoggingPaths.Network;
                    System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] NETERR {line}\n");
            }
            catch { }
        }

        // Invoked by NetworkService on inbound frames
        public async Task OnInboundRequestAsync(string peerUid, string nonce, string displayName)
        {
            // De-dupe: if we already have a pending request from this UID, keep earliest and ignore duplicates
            var trimmedUid = Trim(peerUid);
            if (_uidToNonce.TryGetValue(trimmedUid, out var existingNonce))
            {
                if (_pendingInbound.ContainsKey(existingNonce)) return; // already tracked
            }
            var dn = string.IsNullOrWhiteSpace(displayName) ? trimmedUid : displayName;
            var req = new PendingContactRequest(trimmedUid, nonce, dn, DateTime.UtcNow);
            _pendingInbound[nonce] = req;
            _uidToNonce[trimmedUid] = nonce;
            try { Utilities.Logger.Log($"Inbound contact request queued from {trimmedUid} nonce={nonce}"); } catch { }
            // Fire event (UI will toast / panel)
            try { RequestReceived?.Invoke(req); } catch { }
            await Task.CompletedTask;
        }

        public async Task<bool> AcceptPendingAsync(string nonce)
        {
            try
            {
                if (!_pendingInbound.TryRemove(nonce, out var req)) return false;
                _uidToNonce.TryRemove(req.Uid, out _);
                try { Utilities.Logger.Log($"Contact request ACCEPTED for {req.Uid}"); } catch { }
                await _net.SendContactAcceptAsync(Trim(req.Uid), nonce, CancellationToken.None);
                var dn2 = string.IsNullOrWhiteSpace(req.DisplayName) ? Trim(req.Uid) : req.DisplayName;
                _contacts.AddContact(new Models.Contact { UID = Trim(req.Uid), DisplayName = dn2, ExpectedPublicKeyHex = null }, AppServices.Passphrase);
                AppServices.Peers.IncludeContacts();
                return true;
            }
            catch { return false; }
        }

        public async Task<bool> RejectPendingAsync(string nonce)
        {
            try
            {
                if (!_pendingInbound.TryRemove(nonce, out var req)) return false;
                _uidToNonce.TryRemove(req.Uid, out _);
                try { Utilities.Logger.Log($"Contact request REJECTED for {req.Uid}"); } catch { }
                await _net.SendContactCancelAsync(Trim(req.Uid), nonce, CancellationToken.None);
                return true;
            }
            catch { return false; }
        }

        // TEST: Simulate inbound request (no network). If uid null -> generate ephemeral.
        public void SimulateInboundRequest(string? uid = null, string? displayName = null)
        {
            try
            {
                uid ??= $"usr-TEST{Guid.NewGuid().ToString("N")[..8]}";
                var trimmed = Trim(uid);
                var nonce = Guid.NewGuid().ToString("N");
                var dn = string.IsNullOrWhiteSpace(displayName) ? $"TestUser-{trimmed[..Math.Min(6, trimmed.Length)]}" : displayName!;
                var req = new PendingContactRequest(trimmed, nonce, dn, DateTime.UtcNow);
                _pendingInbound[nonce] = req;
                _uidToNonce[trimmed] = nonce;
                try { Utilities.Logger.Log($"[SIM] Inbound contact request queued from {trimmed} nonce={nonce}"); } catch { }
                try { RequestReceived?.Invoke(req); } catch { }
            }
            catch { }
        }

        // TEST: Clear all pending inbound requests; returns count removed.
        public int ClearAllPendingInbound()
        {
            int count = 0;
            try
            {
                foreach (var kv in _pendingInbound)
                {
                    if (_pendingInbound.TryRemove(kv.Key, out _)) count++;
                }
                _uidToNonce.Clear();
            }
            catch { }
            return count;
        }

        public void OnInboundAccept(string nonce)
        {
            if (_pending.TryGetValue(nonce, out var tcs)) tcs.TrySetResult(ContactRequestResult.Accepted);
        }
        public void OnInboundCancel(string nonce)
        {
            if (_pending.TryGetValue(nonce, out var tcs)) tcs.TrySetResult(ContactRequestResult.Rejected);
        }

        private static string Trim(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
            return uid.StartsWith("usr-", StringComparison.Ordinal) && uid.Length > 4 ? uid.Substring(4) : uid;
        }

        private static string ResolveHost(string uid)
        {
            foreach (var p in AppServices.Peers.Peers) if (string.Equals(Trim(uid), Trim(p.UID), StringComparison.OrdinalIgnoreCase)) return p.Address;
            return "127.0.0.1"; // fallback
        }
        private static int ResolvePort(string uid)
        {
            foreach (var p in AppServices.Peers.Peers) if (string.Equals(Trim(uid), Trim(p.UID), StringComparison.OrdinalIgnoreCase)) return p.Port;
            return AppServices.Settings.Settings.Port;
        }

        // User-initiated verification request: sends 0xC3 frame and records local intent.
        public async Task<bool> RequestVerificationAsync(string uid, CancellationToken ct)
        {
            uid = Trim(uid);
            if (string.IsNullOrWhiteSpace(uid)) return false;
            // Must have an active session (otherwise we cannot send encrypted frame).
            var ok = await _net.SendVerifyIntentAsync(uid, ct);
            if (!ok) return false;
            _verifyInitiated[uid] = true;
            EvaluateMutualVerification(uid);
            return true;
        }

        // Start the manual verification UX with the peer (sends 0xC4). Caller should also open local dialog.
        public async Task<bool> StartManualVerificationAsync(string uid, CancellationToken ct)
        {
            uid = Trim(uid);
            if (string.IsNullOrWhiteSpace(uid)) return false;
            var sent = await _net.SendVerifyRequestAsync(uid, ct);
            if (sent)
            {
                _verifyRequestPending[uid] = true;
            }
            return sent;
        }

        // Cancel a pending manual verification (sends 0xC5) and clears local state.
        public async Task CancelManualVerificationAsync(string uid, CancellationToken ct, string? cancelledBy = null)
        {
            uid = Trim(uid);
            try { await _net.SendVerifyCancelAsync(uid, ct); } catch { }
            _verifyRequestPending.TryRemove(uid, out _);
            try { VerifyRequestCancelled?.Invoke(uid); } catch { }
            if (!string.IsNullOrWhiteSpace(cancelledBy))
            {
                try { Utilities.Logger.Log($"Verification request was cancelled by {cancelledBy}"); } catch { }
            }
        }

        // Inbound 0xC3 frame
        public void OnInboundVerifyIntent(string uid)
        {
            uid = Trim(uid);
            _verifyReceived[uid] = true;
            EvaluateMutualVerification(uid);
        }

        // Inbound 0xC4
        public void OnInboundVerifyRequest(string uid)
        {
            uid = Trim(uid);
            _verifyRequestPending[uid] = true;
            try { VerifyRequestReceived?.Invoke(uid); } catch { }
        }

        // Inbound 0xC5
        public void OnInboundVerifyCancel(string uid)
        {
            uid = Trim(uid);
            _verifyRequestPending.TryRemove(uid, out _);
            try { VerifyRequestCancelled?.Invoke(uid); } catch { }
        }

        private void EvaluateMutualVerification(string uid)
        {
            try
            {
                if (_verifyInitiated.ContainsKey(uid) && _verifyReceived.ContainsKey(uid))
                {
                    // Only mark verified if we have observed a public key for the peer.
                    var peer = AppServices.Peers.Peers.Find(p => string.Equals(p.UID, uid, StringComparison.OrdinalIgnoreCase));
                    if (peer?.PublicKey is { Length: > 0 })
                    {
                        AppServices.Peers.SetPeerVerification(uid, true);
                        AppServices.Contacts.SetPublicKeyVerified(uid, true);
                        // Persist the verified status so the green shield remains across sessions
                        try { AppServices.Contacts.SetIsVerified(uid, true, AppServices.Passphrase); } catch { }
                        try { Utilities.Logger.Log($"Public key verified by mutual intent for {uid}"); } catch { }
                        // User confirmation: toast a small info message (fire-and-forget)
                        try
                        {
                            var name = AppServices.Contacts.Contacts.FirstOrDefault(c => string.Equals(c.UID, uid, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? uid;
                            _ = _dialogs.ShowInfoAsync("Contact Verified", $"You verified {name}.");
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
    }
}
