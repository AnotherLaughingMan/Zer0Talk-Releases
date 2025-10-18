/*
    EventHub: centralized dispatcher for app-wide events.
    - UI layers subscribe here to avoid many direct service subscriptions.
    - Threading: Raise* methods are lightweight; handlers should marshal to UI thread if needed.
*/
using System;

using ZTalk.Models;

namespace ZTalk.Services
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
    // Request the UI to open a conversation for the provided UID (click-to-open)
    public event Action<string>? OpenConversationRequested;

        public void RaiseNatChanged() { try { NatChanged?.Invoke(); } catch { } }
        public void RaiseNetworkListeningChanged(bool isListening, int? port) { try { NetworkListeningChanged?.Invoke(isListening, port); } catch { } }
        public void RaisePeersChanged() { try { PeersChanged?.Invoke(); } catch { } }
        public void RaiseFirewallPrompt(string message) { if (!string.IsNullOrWhiteSpace(message)) { try { FirewallPrompt?.Invoke(message); } catch { } } }
        public void RaiseUiPulse() { try { UiPulse?.Invoke(); } catch { } }
        public void RaiseNetworkConfigChanged() { try { NetworkConfigChanged?.Invoke(); } catch { } }
        public void RaiseRegressionDetected(string message) { if (!string.IsNullOrWhiteSpace(message)) { try { RegressionDetected?.Invoke(message); } catch { } } }
        public void RaiseAllMessagesPurged(MessagePurgeSummary summary) { try { AllMessagesPurged?.Invoke(summary); } catch { } }
    public void RaiseOpenConversationRequested(string uid) { if (string.IsNullOrWhiteSpace(uid)) return; try { OpenConversationRequested?.Invoke(uid); } catch { } }
    }
}
