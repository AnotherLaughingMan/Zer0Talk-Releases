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
    public partial class ContactRequestsService
    {
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
            try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] OnInboundRequest queued uid={trimmedUid} nonce={nonce} count={_pendingInbound.Count}\n"); } catch { }
            // Fire event (UI will toast / panel)
            try { RequestReceived?.Invoke(req); } catch { }
            try
            {
                if (PendingChanged != null) { PendingChanged.Invoke(); }
                else { try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] PendingChanged NO-SUBSCRIBERS (OnInboundRequest) count={_pendingInbound.Count}\n"); } catch { } }
            }
            catch { }
            await Task.CompletedTask;
        }

        public async Task<bool> AcceptPendingAsync(string nonce)
        {
            try
            {
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] AcceptPending START nonce={nonce} currentCountBefore={_pendingInbound.Count}\n"); } catch { }
                if (!_pendingInbound.TryRemove(nonce, out var req))
                {
                    try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] AcceptPending FAILED remove nonce={nonce} currentCount={_pendingInbound.Count}\n"); } catch { }
                    return false;
                }
                _uidToNonce.TryRemove(req.Uid, out _);
                try { Utilities.Logger.Log($"Contact request ACCEPTED for {req.Uid}"); } catch { }
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] AcceptPending removed nonce={nonce} newCount={_pendingInbound.Count}\n"); } catch { }
                try
                {
                    if (PendingChanged != null) { PendingChanged.Invoke(); if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] PendingChanged invoked after accept nonce={nonce} count={_pendingInbound.Count}\n"); }
                    else { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] PendingChanged NO-SUBSCRIBERS after accept nonce={nonce} count={_pendingInbound.Count}\n"); }
                }
                catch { }
                await _net.SendContactAcceptAsync(Trim(req.Uid), nonce, CancellationToken.None);
                var dn2 = string.IsNullOrWhiteSpace(req.DisplayName) ? Trim(req.Uid) : req.DisplayName;
                var contact = new Models.Contact { UID = Trim(req.Uid), DisplayName = dn2, ExpectedPublicKeyHex = null };
                _contacts.AddContact(contact, AppServices.Passphrase);
                AppServices.Peers.IncludeContacts();
                // Try immediate peer verification if peer is present
                TryImmediatePeerVerification(contact);
                return true;
            }
            catch { return false; }
        }

        public async Task<bool> RejectPendingAsync(string nonce)
        {
            try
            {
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] RejectPending START nonce={nonce} currentCountBefore={_pendingInbound.Count}\n"); } catch { }
                if (!_pendingInbound.TryRemove(nonce, out var req))
                {
                    try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] RejectPending FAILED remove nonce={nonce} currentCount={_pendingInbound.Count}\n"); } catch { }
                    return false;
                }
                _uidToNonce.TryRemove(req.Uid, out _);
                try { Utilities.Logger.Log($"Contact request REJECTED for {req.Uid}"); } catch { }
                await _net.SendContactCancelAsync(Trim(req.Uid), nonce, CancellationToken.None);
                try
                {
                    if (PendingChanged != null) { PendingChanged.Invoke(); if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] PendingChanged invoked after reject nonce={nonce} count={_pendingInbound.Count}\n"); }
                    else { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] PendingChanged NO-SUBSCRIBERS after reject nonce={nonce} count={_pendingInbound.Count}\n"); }
                }
                catch { }
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
                try
                {
                    if (PendingChanged != null) { PendingChanged.Invoke(); }
                    else { try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] PendingChanged NO-SUBSCRIBERS (Simulate) count={_pendingInbound.Count}\n"); } catch { } }
                }
                catch { }
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] SimulateInbound queued uid={trimmed} nonce={nonce} count={_pendingInbound.Count}\n"); } catch { }
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
                try
                {
                    if (PendingChanged != null) { PendingChanged.Invoke(); }
                    else { try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] PendingChanged NO-SUBSCRIBERS (ClearAll) newCount={_pendingInbound.Count}\n"); } catch { } }
                }
                catch { }
                try { if (Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [ContactRequests] ClearAllPendingInbound removed={count} newCount={_pendingInbound.Count}\n"); } catch { }
            }
            catch { }
            return count;
        }

        public void OnInboundAccept(string nonce, string accepterUid, string? accepterDisplayName)
        {
            // Automatically add the accepter as a contact (bidirectional add)
            try
            {
                var uid = Trim(accepterUid);
                var dn = string.IsNullOrWhiteSpace(accepterDisplayName) ? uid : accepterDisplayName;
                
                // Check if already a contact to avoid duplicates (compare trimmed UIDs)
                bool alreadyExists = _contacts.Contacts.Any(c => 
                {
                    var contactUid = Trim(c.UID);
                    return string.Equals(contactUid, uid, StringComparison.OrdinalIgnoreCase);
                });
                
                if (!alreadyExists)
                {
                    var contact = new Models.Contact { UID = uid, DisplayName = dn, ExpectedPublicKeyHex = null };
                    bool added = _contacts.AddContact(contact, AppServices.Passphrase);
                    AppServices.Peers.IncludeContacts();
                    try { Utilities.Logger.Log($"Auto-added {dn} ({uid}) to contacts after they accepted our request (added={added})"); } catch { }
                    
                    // Immediate verification if peer is present
                    TryImmediatePeerVerification(contact);
                    
                    // Force UI refresh on main thread to ensure contact list updates immediately
                    if (added)
                    {
                        try
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                try
                                {
                                    // Restore focus to MainWindow to prevent dialogs from blocking updates
                                    var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                                    if (lifetime?.MainWindow is Avalonia.Controls.Window mainWindow)
                                    {
                                        mainWindow.Activate();
                                        mainWindow.Focus();
                                    }
                                    
                                    // Trigger changed event again on UI thread to ensure UI updates
                                    AppServices.Contacts.NotifyChanged();
                                }
                                catch { }
                            }, DispatcherPriority.Normal);
                        }
                        catch { }
                    }
                }
                else
                {
                    try { Utilities.Logger.Log($"Skipped adding {dn} ({uid}) - already a contact"); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { Utilities.Logger.Log($"Failed to auto-add contact on accept: {ex.Message}"); } catch { }
            }
            
            // Complete the pending request
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

        // Inbound 0xC6: Peer has verified us, update our local verification status for them
        public void OnInboundVerifyComplete(string uid)
        {
            uid = Trim(uid);
            try
            {
                // Mark this contact as verified since the peer has confirmed they verified us
                var peer = AppServices.Peers.Peers.Find(p => string.Equals(p.UID, uid, StringComparison.OrdinalIgnoreCase));
                if (peer?.PublicKey is { Length: > 0 })
                {
                    AppServices.Peers.SetPeerVerification(uid, true);
                    AppServices.Contacts.SetPublicKeyVerified(uid, true);
                    try { AppServices.Contacts.SetIsVerified(uid, true, AppServices.Passphrase); } catch { }
                    TryRecordVerificationHistory(uid, peer.PublicKeyHex, "Peer Completion");
                    try { Utilities.Logger.Log($"Verification complete notification received from {uid}"); } catch { }
                    
                    // Brute-force contact list refresh when receiving verification from peer
                    try
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            try 
                            { 
                                // Force immediate contact list refresh for verification status
                                AppServices.Contacts.NotifyChanged();
                                // Double-tap to ensure refresh processes
                                Dispatcher.UIThread.Post(() =>
                                {
                                    try { AppServices.Contacts.NotifyChanged(); } catch { }
                                }, DispatcherPriority.Background);
                            } catch { }
                        }, DispatcherPriority.Send);
                    }
                    catch { }

                    try
                    {
                        AppServices.Notifications.PostSecurityEvent(
                            uid,
                            AppServices.Identity.DisplayName,
                            "Identity verification updated",
                            $"{uid} confirmed verification completion.");
                    }
                    catch { }
                }
            }
            catch { }
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
                        TryRecordVerificationHistory(uid, peer.PublicKeyHex, "Mutual Intent");
                        try { Utilities.Logger.Log($"Public key verified by mutual intent for {uid}"); } catch { }
                        
                        // Send verification complete notification to peer (0xC6)
                        // This ensures both parties see the verification status immediately
                        try
                        {
                            _ = AppServices.Network.SendVerifyCompleteAsync(uid, CancellationToken.None);
                        }
                        catch { }
                        
                        // Brute-force contact list refresh immediately after verification completes
                        try
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                try
                                {
                                    // Force immediate contact list refresh for verification status
                                    AppServices.Contacts.NotifyChanged();
                                    // Double-tap to ensure refresh processes
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        try { AppServices.Contacts.NotifyChanged(); } catch { }
                                    }, DispatcherPriority.Background);
                                }
                                catch { }
                            }, DispatcherPriority.Send);
                        }
                        catch { }
                        
                        // User confirmation: toast a small info message (fire-and-forget)
                        try
                        {
                            var name = AppServices.Contacts.Contacts.FirstOrDefault(c => string.Equals(c.UID, uid, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? uid;
                            _ = _dialogs.ShowSuccessAsync("Contact Verified", $"You verified {name}.");
                        }
                        catch { }

                        try
                        {
                            AppServices.Notifications.PostSecurityEvent(
                                uid,
                                AppServices.Identity.DisplayName,
                                "Identity verified",
                                $"Mutual trust ceremony completed with {uid}.");
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void TryRecordVerificationHistory(string uid, string? publicKeyHex, string method)
        {
            try
            {
                var fingerprint = Utilities.TrustCeremonyFormatter.FingerprintFromPublicKeyHex(publicKeyHex);
                _ = AppServices.Contacts.RecordVerification(uid, AppServices.Passphrase, fingerprint, method);
            }
            catch { }
        }

        // Retry offline contact requests when peer comes online
        public async Task RetryOfflineRequestAsync(string uid)
        {
            uid = Trim(uid);
            if (!_offlineQueue.TryRemove(uid, out var queued)) return;
            
            try
            {
                Utilities.Logger.Log($"Retrying offline contact request for {uid}");
                // Use SendRequestAsync with discovered peer info
                _ = await SendRequestAsync(uid, timeout: TimeSpan.FromSeconds(15));
            }
            catch (Exception ex)
            {
                try { Utilities.Logger.Log($"Failed to retry offline contact request for {uid}: {ex.Message}"); } catch { }
            }
        }

        // Check for offline requests when any peer comes online (call from NetworkService or PeerManager)
        public void OnPeerOnline(string uid)
        {
            uid = Trim(uid);
            if (_offlineQueue.ContainsKey(uid))
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000); // Brief delay to ensure session is established
                    await RetryOfflineRequestAsync(uid);
                });
            }
        }
    }
}
