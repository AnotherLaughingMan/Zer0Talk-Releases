/*
    EventHub: centralized dispatcher for app-wide events.
    - UI layers subscribe here to avoid many direct service subscriptions.
    - Threading: Raise* methods are lightweight; handlers should marshal to UI thread if needed.
*/
using System;

using ZTalk.Models;

namespace P2PTalk.Services
{
    public sealed class EventHub
    {
    public event Action? NatChanged;
        public event Action<bool, int?>? NetworkListeningChanged; // (isListening, port)
        public event Action? PeersChanged;
        public event Action<string>? FirewallPrompt; // info/warnings for UI banners
                                                     // Global UI pulse raised on the UI thread at a configured interval; used to keep visuals fresh even without service events.
        public event Action? UiPulse;
        // Raised when persisted network configuration is changed (e.g., port or MajorNode).
        // Handled by app-level startup or background services, not by UI windows.
        public event Action? NetworkConfigChanged;
        // Regression notifications for developer visibility (e.g., toast/log window)
        public event Action<string>? RegressionDetected;
        // Raised when messages are pruned/expired for a given peer (e.g., retention)
        public event Action<MessagePurgeSummary>? AllMessagesPurged;
        // Raised when an outbound message's delivery metadata changes (e.g., Pending -> Sent)
        public event Action<string, Guid, string?, DateTime?>? OutboundDeliveryUpdated;

        public void RaiseNatChanged() { try { NatChanged?.Invoke(); } catch { } }
        public void RaiseNetworkListeningChanged(bool isListening, int? port) { try { NetworkListeningChanged?.Invoke(isListening, port); } catch { } }
        public void RaisePeersChanged() { try { PeersChanged?.Invoke(); } catch { } }
        public void RaiseFirewallPrompt(string message) { if (!string.IsNullOrWhiteSpace(message)) { try { FirewallPrompt?.Invoke(message); } catch { } } }
        public void RaiseUiPulse() { try { UiPulse?.Invoke(); } catch { } }
        public void RaiseNetworkConfigChanged() { try { NetworkConfigChanged?.Invoke(); } catch { } }
        public void RaiseRegressionDetected(string message) { if (!string.IsNullOrWhiteSpace(message)) { try { RegressionDetected?.Invoke(message); } catch { } } }
        public void RaiseAllMessagesPurged(MessagePurgeSummary summary) { try { AllMessagesPurged?.Invoke(summary); } catch { } }
        public void RaiseOutboundDeliveryUpdated(string peerUid, Guid messageId, string? status, DateTime? deliveredUtc)
        {
            if (string.IsNullOrWhiteSpace(peerUid)) return;
            try { OutboundDeliveryUpdated?.Invoke(peerUid, messageId, status, deliveredUtc); } catch { }
        }
    }
}
