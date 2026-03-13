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
using Avalonia.Threading;
using Models = Zer0Talk.Models;

namespace Zer0Talk.Services
{
    public enum ContactRequestResult
    {
        NotFound,
        Accepted,
        Rejected,
        Timeout,
        Failed
    }

    public partial class ContactRequestsService
    {
        private readonly IdentityService _identity;
        private readonly NetworkService _net;
        private readonly SettingsService _settings;
        private readonly ContactManager _contacts;
        private readonly DialogService _dialogs;
        private string _lastSendDiagnostic = string.Empty;
    // Pending inbound requests (nonce keyed) for UI panel / toast
    private readonly ConcurrentDictionary<string, PendingContactRequest> _pendingInbound = new();
    // Map UID -> latest nonce (helps with de-dupe)
    private readonly ConcurrentDictionary<string, string> _uidToNonce = new(StringComparer.OrdinalIgnoreCase);
    // Verification state (ephemeral, session-based). When both sides indicate intent, mark verified.
    private readonly ConcurrentDictionary<string, bool> _verifyInitiated = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _verifyReceived = new(StringComparer.OrdinalIgnoreCase);
    // Manual verification requests (peer asks to open dialog). Track pending by UID.
    private readonly ConcurrentDictionary<string, bool> _verifyRequestPending = new(StringComparer.OrdinalIgnoreCase);
    // Offline contact request queue: UID -> (nonce, displayName, timestamp)
    private readonly ConcurrentDictionary<string, (string Nonce, string DisplayName, DateTime QueuedAt)> _offlineQueue = new(StringComparer.OrdinalIgnoreCase);

        public ContactRequestsService(IdentityService identity, NetworkService net, SettingsService settings, ContactManager contacts, DialogService dialogs)
        {
            _identity = identity; _net = net; _settings = settings; _contacts = contacts; _dialogs = dialogs;
        }

        // nonce -> completion
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ContactRequestResult>> _pending = new();

    // UI event raised on dispatcher by caller: a new inbound contact request enqueued
    public event Action<PendingContactRequest>? RequestReceived;
    // Raised when the pending inbound collection changes (add/remove/clear)
    public event Action? PendingChanged;
    // Raised when a manual verification request arrives (show in notifications)
    public event Action<string>? VerifyRequestReceived; // arg: uid
    public event Action<string>? VerifyRequestCancelled; // arg: uid

    public IReadOnlyCollection<PendingContactRequest> PendingInboundRequests => _pendingInbound.Values.ToArray();
    public string LastSendDiagnostic => _lastSendDiagnostic;

        // [VERIFY] expectedPublicKeyHex: optional hex public key provided by user to validate identity. Lowercase, no separators.
        // Simple per-UID throttle to avoid spamming the same peer with requests due to UI retries or flaky sessions.
        private readonly ConcurrentDictionary<string, DateTime> _lastOutboundAt = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan OutboundMinInterval = TimeSpan.FromSeconds(5);

        public async Task<ContactRequestResult> SendRequestAsync(string uid, string? host = null, int? port = null, TimeSpan? timeout = null, string? expectedPublicKeyHex = null)
        {
            timeout ??= TimeSpan.FromSeconds(20);
            uid = Trim(uid);
            SetSendDiagnostic($"start uid={uid} host={(string.IsNullOrWhiteSpace(host) ? "<none>" : host)} port={(port.HasValue ? port.Value.ToString() : "<none>")}");
            // Throttle repeated outbound requests to the same UID
            var now = DateTime.UtcNow;
            if (_lastOutboundAt.TryGetValue(uid, out var last) && (now - last) < OutboundMinInterval)
            {
                SetSendDiagnostic($"throttled uid={uid}");
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
                    {
                        host = p.Address;
                        port = p.Port;
                        SetSendDiagnostic($"resolved-from-peers uid={uid} endpoint={host}:{port}");
                        break;
                    }
                }

                // WAN fallback: try registry lookup by UID via configured relay endpoint(s)
                if (host == null || port == null || string.IsNullOrWhiteSpace(host) || port == 0)
                {
                    try
                    {
                        // 12s window: each relay read allows 8s for federated lookup + overhead for sequential relay iteration.
                        using var lookupCts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                        var lookup = await AppServices.WanDirectory.LookupPeerAsync(uid, lookupCts.Token);
                        if (lookup != null)
                        {
                            host = lookup.Host;
                            port = lookup.Port;
                            SetSendDiagnostic($"wan-lookup-hit uid={uid} endpoint={lookup.Host}:{lookup.Port} src={lookup.Source}");

                            try
                            {
                                var merged = new List<Models.Peer>(AppServices.Peers.Peers)
                                {
                                    new Models.Peer { UID = uid, Address = lookup.Host, Port = lookup.Port, Status = "Discovered" }
                                };
                                AppServices.Peers.SetDiscovered(merged);
                            }
                            catch { }
                        }
                        else
                        {
                            SetSendDiagnostic($"wan-lookup-miss uid={uid}");
                        }
                    }
                    catch { }
                }
            }
            using var cts = new CancellationTokenSource(timeout.Value);
            // Step 1: ensure connection + encrypted session
            var connectStart = DateTime.UtcNow;
            SetSendDiagnostic($"connect-attempt uid={Trim(uid)} hintHost={(string.IsNullOrWhiteSpace(host) ? "<none>" : host)} hintPort={(port.HasValue ? port.Value.ToString() : "<none>")}");
            var okRelayOrDirect = await _net.ConnectPeerWithHintsAsync(Trim(uid), host, port, cts.Token);
            if (!okRelayOrDirect)
            {
                SetSendDiagnostic($"connect-fail uid={Trim(uid)}");
                SafeLogNetError($"contact-request connect-fail uid={Trim(uid)} host={host} port={port}");
                // Queue for retry when peer comes online
                var offlineNonce = Guid.NewGuid().ToString("N");
                var offlineDisplayName = _identity.DisplayName ?? string.Empty;
                _offlineQueue[Trim(uid)] = (offlineNonce, offlineDisplayName, DateTime.UtcNow);
                try { Utilities.Logger.Log($"Queued offline contact request for {Trim(uid)} - will retry when online"); } catch { }
                return ContactRequestResult.NotFound;
            }
            // Wait a short grace period for session registration (handshake completion)
            var sessionReady = await _net.WaitForEncryptedSessionAsync(Trim(uid), TimeSpan.FromSeconds(6), cts.Token);
            if (!sessionReady)
            {
                SetSendDiagnostic($"no-session uid={Trim(uid)}");
                SafeLogNetError($"contact-request no-session uid={Trim(uid)} after-connect elapsed={(DateTime.UtcNow-connectStart).TotalMilliseconds:F0}ms");
                return ContactRequestResult.Failed;
            }
            SetSendDiagnostic($"session-ready uid={Trim(uid)}");

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
                    SetSendDiagnostic($"send-fail uid={uid} attempts={attempt}");
                    SafeLogNetError($"contact-request send-fail uid={uid} attempts={attempt}");
                    return ContactRequestResult.Failed;
                }
                SetSendDiagnostic($"request-sent uid={uid} nonce={nonce}");
                // Await accept/cancel
                using var _ = cts.Token.Register(() => tcs.TrySetResult(ContactRequestResult.Timeout));
                var result = await tcs.Task;
                SetSendDiagnostic($"result uid={uid} outcome={result}");
                // OnInboundAccept will have already added the contact when C1 frame arrives
                // If successful, just update ExpectedPublicKeyHex if provided and verify
                if (result == ContactRequestResult.Accepted && !string.IsNullOrWhiteSpace(expectedPublicKeyHex))
                {
                    try
                    {
                        var contact = _contacts.Contacts.FirstOrDefault(c => string.Equals(c.UID, uid, StringComparison.OrdinalIgnoreCase));
                        if (contact != null)
                        {
                            var normalized = NormalizeHex(expectedPublicKeyHex);
                            if (normalized != contact.ExpectedPublicKeyHex)
                            {
                                contact.ExpectedPublicKeyHex = normalized;
                                _contacts.Save(AppServices.Passphrase);
                            }
                            TryImmediatePeerVerification(contact);
                        }
                    }
                    catch { }
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

        private void SetSendDiagnostic(string message)
        {
            _lastSendDiagnostic = message;
            try { Utilities.Logger.Log($"[ContactRequest] {message}"); } catch { }
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

    }
}
