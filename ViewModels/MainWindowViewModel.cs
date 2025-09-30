/*
    Main window VM: binds contacts and message list; orchestrates send/receive actions.
    - Exposes commands for opening Settings/Network windows.
*/
// TODO[ANCHOR]: MainWindowVM - Bind contacts/messages and actions
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions; // [PRIVACY] Diagnostics text sanitization
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Media;

using P2PTalk.Containers;
using ZTalk.Models;
using Models = ZTalk.Models;
using P2PTalk.Services;
using P2PTalk.Utilities;

namespace P2PTalk.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        // Expose a debug-only UI flag for XAML visibility bindings
        public bool IsDebugUi
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        public string PrototypeBadgeText => P2PTalk.AppInfo.PrototypeBadgeText;

        public ObservableCollection<Contact> Contacts { get; } = new();
        public ObservableCollection<Message> Messages { get; } = new();
        public ObservableCollection<object> TimelineItems { get; } = new();
        private double _chatViewportWidth;
        public double ChatViewportWidth
        {
            get => _chatViewportWidth;
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) return;
                if (Math.Abs(value - _chatViewportWidth) < 0.5) return;
                _chatViewportWidth = value;
                OnPropertyChanged();
                try
                {
                    if (LoggingPaths.Enabled)
                    {
                        var line = $"{DateTime.Now:O} [UI][ChatWidth] viewport={value:F1}";
                        System.IO.File.AppendAllText(LoggingPaths.UI, line + Environment.NewLine);
                    }
                }
                catch { }
            }
        }
    private readonly MessageContainer _messagesStore = new();
    private DateTime? _lastTimelineDate;
        private const string UpdatesKey = "MainWindow.UI";
    private readonly List<Action> _teardownActions = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _linkPreviewFetchTokens = new();
    private readonly object _linkPreviewLock = new();
        private bool _disposed;
    // UI tick token to force bindings refresh on central UI pulse
    private long _uiTick;
    public long UiTick { get => _uiTick; private set { _uiTick = value; OnPropertyChanged(); } }

        // Debounce for Contacts.Changed to avoid UI churn on presence flaps
        private CancellationTokenSource? _contactsRefreshCts;
        private DateTime _lastContactsRefreshScheduledAtUtc = DateTime.MinValue;
        private static readonly TimeSpan ContactsRefreshDebounce = TimeSpan.FromMilliseconds(180);

        // Selection-freeze guard to prevent focus shifts during transient UI (context/hover)
        private int _selectionFreezeCount;
        private bool _selectionReconcilePending;
        public bool IsSelectionFrozen => System.Threading.Volatile.Read(ref _selectionFreezeCount) > 0;
        public void BeginSelectionFreeze()
        {
            var v = System.Threading.Interlocked.Increment(ref _selectionFreezeCount);
            try
            {
                if (P2PTalk.Utilities.LoggingPaths.Enabled)
                {
                    var line = $"{DateTime.Now:O} [UI] Freeze begin: reason=context/hover count={v}";
                    System.IO.File.AppendAllText(P2PTalk.Utilities.LoggingPaths.UI, line + Environment.NewLine);
                }
            }
            catch { }
        }
        public void EndSelectionFreeze()
        {
            var v = System.Threading.Interlocked.Decrement(ref _selectionFreezeCount);
            if (v <= 0)
            {
                _selectionFreezeCount = 0;
                try
                {
                    if (P2PTalk.Utilities.LoggingPaths.Enabled)
                    {
                        var line = $"{DateTime.Now:O} [UI] Freeze end";
                        System.IO.File.AppendAllText(P2PTalk.Utilities.LoggingPaths.UI, line + Environment.NewLine);
                    }
                }
                catch { }
                if (_selectionReconcilePending)
                {
                    _selectionReconcilePending = false;
                    // Re-run a debounced refresh to reconcile selection safely
                    ScheduleContactsRefresh();
                }
            }
        }

        private void ScheduleContactsRefresh()
        {
            try
            {
                _contactsRefreshCts?.Cancel();
            }
            catch { }
            _contactsRefreshCts = new CancellationTokenSource();
            var token = _contactsRefreshCts.Token;
            _lastContactsRefreshScheduledAtUtc = DateTime.UtcNow;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(ContactsRefreshDebounce, token);
                    if (token.IsCancellationRequested) return;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            // If selection is frozen, defer any list mutations
                            if (IsSelectionFrozen)
                            {
                                _selectionReconcilePending = true;
                                return;
                            }

                            var prevUid = SelectedContact?.UID;

                            // Optimize: avoid clearing/readding when membership unchanged; update presence in-place
                            var current = Contacts.ToDictionary(c => c.UID, c => c);
                            var source = AppServices.Contacts.Contacts.ToDictionary(c => c.UID, c => c);
                            // Remove contacts no longer present
                            foreach (var uid in current.Keys.Except(source.Keys).ToList())
                            {
                                var rem = Contacts.FirstOrDefault(c => c.UID == uid);
                                if (rem != null) { Contacts.Remove(rem); }
                            }
                            // Add new contacts and update presence/display name for existing
                            foreach (var kv in source)
                            {
                                if (!current.TryGetValue(kv.Key, out var existing))
                                {
                                    Contacts.Add(kv.Value);
                                }
                                else
                                {
                                    if (existing.Presence != kv.Value.Presence)
                                        existing.Presence = kv.Value.Presence;
                                    if (!string.Equals(existing.DisplayName, kv.Value.DisplayName, StringComparison.Ordinal))
                                    {
                                        existing.DisplayName = kv.Value.DisplayName;
                                    }
                                }
                            }
                            // Decouple presence from layout: avoid any list regrouping; UI binds directly to Contacts
                            if (!IsSelectionFrozen)
                            {
                                if (!string.IsNullOrWhiteSpace(prevUid))
                                {
                                    var still = Contacts.FirstOrDefault(x => string.Equals(x.UID, prevUid, StringComparison.OrdinalIgnoreCase));
                                    if (still != null) SelectedContact = still; else SelectedContact = Contacts.Count > 0 ? Contacts[0] : null;
                                }
                                else if (SelectedContact == null)
                                {
                                    SelectedContact = Contacts.Count > 0 ? Contacts[0] : null;
                                }
                            }
                            // Only notify dependent properties; avoid redundant SelectedContact change when reference unchanged
                            OnPropertyChanged(nameof(SelectedContactPublicKeyHex));
                            OnPropertyChanged(nameof(IsChatEncrypted));
                            try
                            {
                                var sel = SelectedContact;
                                if (sel != null && sel.Presence != PresenceStatus.Offline)
                                    StartOfflineBannerFadeOut();
                            }
                            catch { }
                            try
                            {
                                if (P2PTalk.Utilities.LoggingPaths.Enabled)
                                {
                                    var frozen = IsSelectionFrozen ? "true" : "false";
                                    var preserved = (!string.IsNullOrWhiteSpace(prevUid) && string.Equals(prevUid, SelectedContact?.UID, StringComparison.OrdinalIgnoreCase)) ? "true" : "false";
                                    var line = $"{DateTime.Now:O} [UI] Contacts refresh (debounced): items={Contacts.Count} selPrev={prevUid ?? "none"} selNow={SelectedContact?.UID ?? "none"} preserved={preserved} frozen={frozen}";
                                    System.IO.File.AppendAllText(P2PTalk.Utilities.LoggingPaths.UI, line + Environment.NewLine);
                                }
                            }
                            catch { }
                        }
                        catch { }
                    });
                }
                catch { }
            }, token);
        }

        private string _loggedInUsername = string.Empty;
        public string LoggedInUsername
        {
            get => _loggedInUsername;
            set { if (_loggedInUsername != value) { _loggedInUsername = value; OnPropertyChanged(); } }
        }

        public string LoggedInUidShort
        {
            get
            {
                try
                {
                    var uid = TrimUidPrefix(AppServices.Identity.UID ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
                    return uid.Length > 12 ? uid[..12] : uid;
                }
                catch { return string.Empty; }
            }
        }
        public string LoggedInUidFull
        {
            get
            {
                try { return TrimUidPrefix(AppServices.Identity.UID ?? string.Empty); } catch { return string.Empty; }
            }
        }

        private Contact? _selectedContact;
        public Contact? SelectedContact
        {
            get => _selectedContact;
            set
            {
                if (_selectedContact != value)
                {
                    _selectedContact = value;
                    OnPropertyChanged();
                    // Reset per-contact inline banners when switching selection
                    OfflineBannerVisible = false;
                    OnPropertyChanged(nameof(SelectedContactIdentity));
                    OnPropertyChanged(nameof(SelectedContactDisplayName));
                    OnPropertyChanged(nameof(SelectedContactPublicKeyHex));
                    OnPropertyChanged(nameof(IsChatEncrypted));
                    if (_selectedContact != null)
                    {
                        LoadConversation(_selectedContact.UID);
                        // Mini-profile removed; still update dependent props
                        OnPropertyChanged(nameof(IsSelectedContactSimulated));
                        EditableDisplayName = _selectedContact.DisplayName ?? string.Empty;
                        (SaveSimulatedProfileCommand as RelayCommand)?.RaiseCanExecuteChanged();
                        (BurnConversationCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    }
                    else
                    {
                        (BurnConversationCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    }
                }
            }
        }

        private string _outgoingMessage = string.Empty;
        public string OutgoingMessage
        {
            get => _outgoingMessage;
            set
            {
                if (_outgoingMessage != value)
                {
                    _outgoingMessage = value;
                    OnPropertyChanged();
                    (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand SendCommand { get; }
        public ICommand AddContactCommand { get; }
        public ICommand TrustContactCommand { get; }
        public ICommand UntrustContactCommand { get; }
        public ICommand ShowFullProfileCommand { get; }
        public ICommand CloseFullProfileCommand { get; }
        public ICommand SaveSimulatedProfileCommand { get; }
    public ICommand RemoveContactCommand { get; }
    public ICommand BurnConversationCommand { get; }
    public ICommand OpenLinkCommand { get; }
    public ICommand EditMessageCommand { get; }
    public ICommand DeleteMessageCommand { get; }
        // Simulated contact presence controls (context menu)
        public ICommand SetSimulatedContactOnlineCommand { get; }
        public ICommand SetSimulatedContactOfflineCommand { get; }
        // [PORT-ALERT] Dismiss command for port conflict toast
        public ICommand DismissPortAlertCommand { get; }
        // [PRIVACY] Toggle diagnostics sensitive info (ports, IPs) visibility
        public ICommand ToggleDiagnosticsSensitiveCommand { get; }
        public ICommand RetryNatVerificationCommand => new RelayCommand(async _ => { try { await AppServices.Nat.RetryVerificationAsync(); } catch { } });
    // Test commands (Debug only UI wiring expected)
    public ICommand SimulateInviteCommand { get; }
    public ICommand ClearInvitesCommand { get; }

    private bool _hasPendingInvites;
    public bool HasPendingInvites { get => _hasPendingInvites; private set { if (_hasPendingInvites != value) { _hasPendingInvites = value; OnPropertyChanged(); } } }

        // Live avatar image from IdentityService
        private Avalonia.Media.IImage? _identityAvatarImage;
        public Avalonia.Media.IImage? IdentityAvatarImage { get => _identityAvatarImage; private set { _identityAvatarImage = value; OnPropertyChanged(); } }

        private string _addContactInput = string.Empty;
        public string AddContactInput { get => _addContactInput; set { _addContactInput = value; OnPropertyChanged(); (AddContactCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }
        private string _errorMessage = string.Empty;
        public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }

        public MainWindowViewModel()
        {
            // Prefer Display Name for visual identity; never show raw username unless no display name exists
            try { LoggedInUsername = !string.IsNullOrWhiteSpace(AppServices.Settings.Settings.DisplayName) ? AppServices.Settings.Settings.DisplayName : (!string.IsNullOrWhiteSpace(AppServices.Identity.Username) ? AppServices.Identity.Username : "User"); }
            catch { LoggedInUsername = "User"; }
            Messages.CollectionChanged += Messages_CollectionChanged;
            _teardownActions.Add(() => Messages.CollectionChanged -= Messages_CollectionChanged);
            // Reflect current identity (display name + avatar) and react to changes
            try
            {
                RefreshIdentityBindings();
                Action identityChangedHandler = () => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshIdentityBindings);
                AppServices.Identity.Changed += identityChangedHandler;
                _teardownActions.Add(() => AppServices.Identity.Changed -= identityChangedHandler);
            }
            catch { }

            // Load contacts from manager
            AppServices.Contacts.Load(AppServices.Passphrase);
            foreach (var c in AppServices.Contacts.Contacts) Contacts.Add(c);
            Action contactsChangedHandler = () => { ScheduleContactsRefresh(); };
            AppServices.Contacts.Changed += contactsChangedHandler;
            _teardownActions.Add(() => AppServices.Contacts.Changed -= contactsChangedHandler);
            SelectedContact = Contacts.Count > 0 ? Contacts[0] : null;

            SendCommand = new RelayCommand(_ => SendMessage(), _ => CanSend());
            AddContactCommand = new RelayCommand(_ => AddContact(), _ => CanAddContact());
            SetSimulatedContactOnlineCommand = new RelayCommand(p =>
            {
                var uid = (p as string) ?? SelectedContact?.UID;
                if (string.IsNullOrWhiteSpace(uid)) return;
                try { AppServices.Contacts.SetPresence(uid!, Models.PresenceStatus.Online, System.TimeSpan.FromSeconds(60), Models.PresenceSource.Manual); } catch { }
                try { DrainSimulatedPending(uid!); } catch { }
            });
            SetSimulatedContactOfflineCommand = new RelayCommand(p =>
            {
                var uid = (p as string) ?? SelectedContact?.UID;
                if (string.IsNullOrWhiteSpace(uid)) return;
                try { AppServices.Contacts.SetPresence(uid!, Models.PresenceStatus.Offline, System.TimeSpan.FromSeconds(5), Models.PresenceSource.Manual); } catch { }
            });
            TrustContactCommand = new RelayCommand(p =>
            {
                var uid = (p as string) ?? SelectedContact?.UID;
                if (string.IsNullOrWhiteSpace(uid)) return;
                try
                {
                    // Persist to contacts first (source of truth) and mirror to peers
                    var ok = AppServices.Contacts.SetTrusted(uid!, true, AppServices.Passphrase);
                    AppServices.Peers.SetTrusted(uid!, true);
                    // Immediate UI reflect for current selection without waiting for Changed event
                    if (ok && SelectedContact != null && string.Equals(SelectedContact.UID, uid, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedContact.IsTrusted = true;
                        OnPropertyChanged(nameof(SelectedContact));
                    }
                }
                catch { }
            });
            UntrustContactCommand = new RelayCommand(p =>
            {
                var uid = (p as string) ?? SelectedContact?.UID;
                if (string.IsNullOrWhiteSpace(uid)) return;
                try
                {
                    var ok = AppServices.Contacts.SetTrusted(uid!, false, AppServices.Passphrase);
                    AppServices.Peers.SetTrusted(uid!, false);
                    if (ok && SelectedContact != null && string.Equals(SelectedContact.UID, uid, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedContact.IsTrusted = false;
                        OnPropertyChanged(nameof(SelectedContact));
                    }
                }
                catch { }
            });
            ShowFullProfileCommand = new RelayCommand(p => {
                try
                {
                    var targetUid = p as string;
                    if (!string.IsNullOrWhiteSpace(targetUid))
                    {
                        var match = Contacts.FirstOrDefault(x => string.Equals(x.UID, targetUid, StringComparison.OrdinalIgnoreCase));
                        if (match != null) SelectedContact = match;
                    }
                    IsFullProfileOpen = true;
                    EditableDisplayName = SelectedContact?.DisplayName ?? string.Empty;
                }
                catch { IsFullProfileOpen = true; }
            });
            CloseFullProfileCommand = new RelayCommand(_ => { IsFullProfileOpen = false; });
            SaveSimulatedProfileCommand = new RelayCommand(_ => SaveSimulatedProfile(), _ => CanSaveSimulatedProfile());
            RemoveContactCommand = new RelayCommand(p =>
            {
                try
                {
                    var uid = (p as string) ?? SelectedContact?.UID;
                    if (string.IsNullOrWhiteSpace(uid)) return;
                    var wasSelected = string.Equals(SelectedContact?.UID, uid, StringComparison.OrdinalIgnoreCase);
                    // Remove from persistent store; UI updates via Changed event
                    AppServices.Contacts.RemoveContact(uid!, AppServices.Passphrase);
                    if (wasSelected)
                    {
                        // Clear messages immediately so we don't show an orphaned chat while selection settles
                        Messages.Clear();
                    }
                }
                catch { }
            });
            BurnConversationCommand = new RelayCommand(async _ =>
            {
                await BurnConversationAsync();
            }, _ => SelectedContact != null);
            OpenLinkCommand = new RelayCommand(p =>
            {
                try
                {
                    var target = p as string;
                    if (string.IsNullOrWhiteSpace(target) && p is LinkPreview preview)
                    {
                        target = preview.Url;
                    }

                    if (string.IsNullOrWhiteSpace(target)) return;

                    UrlLauncher.TryOpen(target);
                }
                catch { }
            });
            DismissPortAlertCommand = new RelayCommand(_ => { _portAlertCts?.Cancel(); PortAlertVisible = false; });
            ToggleDiagnosticsSensitiveCommand = new RelayCommand(_ => { DiagnosticsSensitive = !DiagnosticsSensitive; NotifyDiagnostics(); });
            SimulateInviteCommand = new RelayCommand(_ => { try { AppServices.ContactRequests.SimulateInboundRequest(); HasPendingInvites = AppServices.ContactRequests.PendingInboundRequests.Count > 0; } catch { } });
            ClearInvitesCommand = new RelayCommand(_ => { try { AppServices.ContactRequests.ClearAllPendingInbound(); HasPendingInvites = false; } catch { } });

            EditMessageCommand = new RelayCommand(async p =>
            {
                try
                {
                    if (SelectedContact == null) return;
                    if (p is not Guid id) return;
                    var msg = Messages.FirstOrDefault(m => m.Id == id);
                    if (msg == null) return;
                    if (!IsOwnMessage(msg)) return; // gate editing to own messages
                    // Allow unlimited editing for messages that are still Pending (not sent yet)
                    var isPending = string.Equals(msg.DeliveryStatus, "Pending", StringComparison.OrdinalIgnoreCase);
                    if (!isPending)
                    {
                        if (!IsWithinEditWindow(msg)) { await AppServices.Dialogs.ShowInfoAsync("Edit Time Expired", "That message can't be edited anymore because the 25-minute window has passed."); return; }
                    }
                    // Simple edit prompt using DialogService
                    var original = msg.Content ?? string.Empty;
                    var newContent = await AppServices.Dialogs.PromptAsync("Edit Message", original);
                    if (newContent == null) return; // cancelled
                    newContent = newContent.TrimEnd();
                    if (newContent == original) return;
                    // Re-check existence before applying edit
                    var stillThere = Messages.Any(m => m.Id == id);
                    if (!stillThere)
                    {
                        await AppServices.Dialogs.ShowInfoAsync("Message Deleted", "Sorry but this message was deleted while you were editing it, it will be purged as soon as you finish editing it.");
                        return;
                    }
                    if (!isPending)
                    {
                        if (!IsWithinEditWindow(msg))
                        {
                            await AppServices.Dialogs.ShowInfoAsync("Edit Time Expired", "That message can't be edited anymore because the 25-minute window has passed.");
                            return;
                        }
                    }
                    var peerUid = SelectedContact.UID;
                    msg.Content = newContent;
                    msg.IsEdited = true;
                    msg.EditedUtc = DateTime.UtcNow;
                    EnsureLinkPreviewProbe(msg, forceRefresh: true);
                    OnPropertyChanged(nameof(Messages)); // ensure bindings refresh
                    _messagesStore.UpdateMessage(peerUid, id, newContent, AppServices.Passphrase);
                    // Also update any queued copy of the message
                    AppServices.OutboxUpdateIfQueued(peerUid, id, newContent, AppServices.Passphrase);
                    var queuedEarly = !AppServices.Network.HasEncryptedSession(peerUid);
                    if (queuedEarly)
                    {
                        AppServices.OutboxQueueEdit(peerUid, id, newContent, AppServices.Passphrase);
                    }
                    // Attempt to propagate edit to recipient if online (best-effort)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var sent = await AppServices.Network.SendEditMessageAsync(peerUid, id, newContent, CancellationToken.None);
                            if (sent)
                            {
                                AppServices.OutboxCancelIfQueued(peerUid, id, AppServices.Passphrase);
                            }
                            else if (!queuedEarly)
                            {
                                AppServices.OutboxQueueEdit(peerUid, id, newContent, AppServices.Passphrase);
                            }
                        }
                        catch
                        {
                            if (!queuedEarly)
                            {
                                AppServices.OutboxQueueEdit(peerUid, id, newContent, AppServices.Passphrase);
                            }
                        }
                    });
                }
                catch { }
            });

            DeleteMessageCommand = new RelayCommand(async p =>
            {
                try
                {
                    if (SelectedContact == null) return;
                    if (p is not Guid id) return;
                    var msg = Messages.FirstOrDefault(m => m.Id == id);
                    if (msg == null) return;
                    var isOwn = IsOwnMessage(msg);
                    var prompt = isOwn
                        ? "Are you sure you want to delete this message? This will delete it for both you and the recipient."
                        : "Delete this message locally? The sender will not be notified.";
                    var ok = await AppServices.Dialogs.ConfirmAsync("Delete Message", prompt, "Delete", "Cancel");
                    if (!ok) return;
                    // Remove from UI first (user-deleted should be treated as truly deleted)
                    Messages.Remove(msg);
                    _messagesStore.DeleteMessage(SelectedContact.UID, id, AppServices.Passphrase);
                    try { LogManualDelete(SelectedContact.UID, id, "manual"); } catch { }
                    if (isOwn)
                    {
                        // Best-effort: also remove from outbox if queued
                        try { AppServices.OutboxCancelIfQueued(SelectedContact.UID, id, AppServices.Passphrase); } catch { }
                        // Attempt to propagate delete to recipient if online (best-effort)
                        _ = Task.Run(() => { try { AppServices.Network.SendDeleteMessageAsync(SelectedContact.UID, id, CancellationToken.None); } catch { } });
                    }
                    // Undo toast (brief window)
                    try
                    {
                        await AppServices.Dialogs.ShowInfoAsync("Message deleted", "Undo is available for a few seconds.", dismissAfterMs: 2200);
                        // NOTE: Implementing interactive undo requires a custom toast with a button. Placeholder for now.
                    }
                    catch { }
                }
                catch { }
            });

            void LogManualDelete(string peerUid, Guid messageId, string reason)
            {
                try
                {
                    if (!P2PTalk.Utilities.LoggingPaths.Enabled) return;
                    var line = $"[RETENTION] {DateTime.Now:O}: User delete {reason} peer={peerUid} id={messageId}";
                    System.IO.File.AppendAllText(P2PTalk.Utilities.LoggingPaths.Retention, line + Environment.NewLine);
                }
                catch { }
            }

            // Watch avatar cache to update bindings
            try
            {
                AvatarCache.Start();
                Action<string> avatarChangedHandler = _ =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { AvatarVersion++; OnPropertyChanged(nameof(AvatarVersion)); });
                AvatarCache.AvatarChanged += avatarChangedHandler;
                _teardownActions.Add(() => AvatarCache.AvatarChanged -= avatarChangedHandler);
            }
            catch { }

            // Load messages for initial contact
            if (SelectedContact != null) LoadConversation(SelectedContact.UID);
            // Subscribe to inbound chat events
            try
            {
                Action<string, Guid, string> chatReceivedHandler = (peerUid, id, content) =>
                {
                    var now = DateTime.UtcNow;
                    var senderUid = TrimUidPrefix(peerUid ?? string.Empty);
                    var recipientUid = TrimUidPrefix(AppServices.Identity.UID ?? string.Empty);
                    var msg = new Message
                    {
                        Id = id,
                        SenderUID = senderUid,
                        RecipientUID = recipientUid,
                        Content = content,
                        Timestamp = now,
                        ReceivedUtc = now,
                        DeliveryStatus = "Received",
                        DeliveredUtc = now,
                        Signature = Array.Empty<byte>(),
                        SenderPublicKey = Array.Empty<byte>(),
                    };
                    var selectedUid = SelectedContact?.UID ?? string.Empty;
                    if (string.Equals(TrimUidPrefix(selectedUid), senderUid, StringComparison.OrdinalIgnoreCase))
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => Messages.Add(msg));
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            OnPropertyChanged(nameof(IsChatEncrypted));
                            MarkMessagesAsRead(new[] { msg });
                        });
                    }
                    _messagesStore.StoreMessage(senderUid, msg, AppServices.Passphrase);
                };
                AppServices.Network.ChatMessageReceived += chatReceivedHandler;
                _teardownActions.Add(() => AppServices.Network.ChatMessageReceived -= chatReceivedHandler);
            }
            catch { }

            // Presence updates are surfaced via ContactManager.Changed when SetPresence is called.

            // Initial diagnostics snapshot
            NotifyDiagnostics();

            // Keep diagnostics live: subscribe to NAT and network listening events and marshal to UI thread
            try
            {
                Action natChangedHandler = () => Avalonia.Threading.Dispatcher.UIThread.Post(() => NotifyDiagnostics());
                AppServices.Events.NatChanged += natChangedHandler;
                _teardownActions.Add(() => AppServices.Events.NatChanged -= natChangedHandler);
            }
            catch { }
            try
            {
                Action<bool, int?> networkListeningHandler = (_, __) => Avalonia.Threading.Dispatcher.UIThread.Post(() => NotifyDiagnostics());
                AppServices.Events.NetworkListeningChanged += networkListeningHandler;
                _teardownActions.Add(() => AppServices.Events.NetworkListeningChanged -= networkListeningHandler);
            }
            catch { }
            // Refresh selected contact's public key display when peers change (e.g., observed key appears)
            try
            {
                Action peersChangedHandler = () => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SelectedContactPublicKeyHex)));
                AppServices.Events.PeersChanged += peersChangedHandler;
                _teardownActions.Add(() => AppServices.Events.PeersChanged -= peersChangedHandler);
            }
            catch { }
            // Also hook direct service events as a fallback, in case EventHub wiring changes
            try
            {
                Action natServiceChangedHandler = () => Avalonia.Threading.Dispatcher.UIThread.Post(() => NotifyDiagnostics());
                AppServices.Nat.Changed += natServiceChangedHandler;
                _teardownActions.Add(() => AppServices.Nat.Changed -= natServiceChangedHandler);
            }
            catch { }
            try
            {
                Action<bool, int?> listeningChangedHandler = (_, __) => Avalonia.Threading.Dispatcher.UIThread.Post(() => NotifyDiagnostics());
                AppServices.Network.ListeningChanged += listeningChangedHandler;
                _teardownActions.Add(() => AppServices.Network.ListeningChanged -= listeningChangedHandler);
            }
            catch { }
            // Also react to handshake completion to refresh encryption indicator
            try
            {
                Action<bool, string, string?> handshakeCompletedHandler = (_, __, ___) => Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IsChatEncrypted)));
                AppServices.Network.HandshakeCompleted += handshakeCompletedHandler;
                _teardownActions.Add(() => AppServices.Network.HandshakeCompleted -= handshakeCompletedHandler);
            }
            catch { }
            // Contact request notifications
            try
            {
                Action<ContactRequestsService.PendingContactRequest> requestReceivedHandler = _ =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        HasPendingInvites = AppServices.ContactRequests.PendingInboundRequests.Count > 0;
                    });
                AppServices.ContactRequests.RequestReceived += requestReceivedHandler;
                _teardownActions.Add(() => AppServices.ContactRequests.RequestReceived -= requestReceivedHandler);
            }
            catch { }

            // Remote edits: update message content if visible
            try
            {
                Action<string, Guid, string> chatEditedHandler = (peerUid, id, text) =>
                {
                    if (SelectedContact?.UID != peerUid) return;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        var m = Messages.FirstOrDefault(x => x.Id == id);
                        if (m != null) { m.Content = text; m.IsEdited = true; m.EditedUtc = DateTime.UtcNow; }
                        OnPropertyChanged(nameof(Messages));
                    });
                };
                AppServices.Network.ChatMessageEdited += chatEditedHandler;
                _teardownActions.Add(() => AppServices.Network.ChatMessageEdited -= chatEditedHandler);

                Action<string, Guid> chatDeletedHandler = (peerUid, id) =>
                {
                    if (SelectedContact?.UID != peerUid) return;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        var m = Messages.FirstOrDefault(x => x.Id == id);
                        if (m != null) Messages.Remove(m);
                    });
                };
                AppServices.Network.ChatMessageDeleted += chatDeletedHandler;
                _teardownActions.Add(() => AppServices.Network.ChatMessageDeleted -= chatDeletedHandler);
            }
            catch { }
            // Received ACKs: mark as Sent (delivery confirmed) and stamp DeliveredUtc
            try
            {
                Action<string, Guid> chatAckHandler = (peerUid, id) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        var m = Messages.FirstOrDefault(x => x.Id == id);
                        if (m != null)
                        {
                            // Only upgrade Pending/Sending to Sent. Do not downgrade Read.
                            if (!string.Equals(m.DeliveryStatus, "Read", StringComparison.OrdinalIgnoreCase))
                            {
                                m.DeliveryStatus = "Sent";
                            }
                            if (m.DeliveredUtc == null)
                            {
                                m.DeliveredUtc = DateTime.UtcNow;
                            }
                            try { _messagesStore.UpdateDelivery(peerUid, id, m.DeliveryStatus, m.DeliveredUtc, AppServices.Passphrase, m.ReadUtc); } catch { }
                            try { Logger.NetworkLog($"UI-Ack: message marked Sent in UI | peer={peerUid} | id={id}"); } catch { }
                        }
                        else
                        {
                            try { Logger.NetworkLog($"UI-Ack: message not found in UI message list | peer={peerUid} | id={id}"); } catch { }
                        }
                    });
                };
                AppServices.Network.ChatMessageReceivedAcked += chatAckHandler;
                _teardownActions.Add(() => AppServices.Network.ChatMessageReceivedAcked -= chatAckHandler);
            }
            catch { }
            try
            {
                Action<string, Guid, string?, DateTime?> outboundDeliveryHandler = (peerUid, id, status, deliveredUtc) =>
                {
                    if (SelectedContact?.UID != peerUid) return;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        var m = Messages.FirstOrDefault(x => x.Id == id);
                        if (m == null) return;
                        var resolvedStatus = string.IsNullOrWhiteSpace(status) ? "Sent" : status;
                        m.DeliveryStatus = resolvedStatus;
                        if (string.Equals(resolvedStatus, "Read", StringComparison.OrdinalIgnoreCase))
                        {
                            var stamp = deliveredUtc ?? DateTime.UtcNow;
                            m.ReadUtc = stamp;
                            if (m.DeliveredUtc == null)
                            {
                                m.DeliveredUtc = stamp;
                            }
                        }
                        else
                        {
                            if (deliveredUtc.HasValue)
                            {
                                m.DeliveredUtc = deliveredUtc;
                            }
                            else if (string.Equals(resolvedStatus, "Sent", StringComparison.OrdinalIgnoreCase) && m.DeliveredUtc == null)
                            {
                                m.DeliveredUtc = DateTime.UtcNow;
                            }
                        }
                        try { Logger.NetworkLog($"UI-DeliveryUpdate: peer={peerUid} id={id} status={resolvedStatus} deliveredUtc={(m.DeliveredUtc.HasValue ? m.DeliveredUtc.Value.ToString("o") : "null")} readUtc={(m.ReadUtc.HasValue ? m.ReadUtc.Value.ToString("o") : "null")} "); } catch { }
                        OnPropertyChanged(nameof(Messages));
                    });
                };
                AppServices.Events.OutboundDeliveryUpdated += outboundDeliveryHandler;
                _teardownActions.Add(() => AppServices.Events.OutboundDeliveryUpdated -= outboundDeliveryHandler);
            }
            catch { }
            // Subscribe to centralized UI pulse to refresh ticking countdowns
            try
            {
                Action uiPulseHandler = () =>
                {
                    UiTick++;
                    OnPropertyChanged(nameof(Messages));
                };
                AppServices.Events.UiPulse += uiPulseHandler;
                _teardownActions.Add(() => AppServices.Events.UiPulse -= uiPulseHandler);
            }
            catch { }
            try
            {
                Action<MessagePurgeSummary> allMessagesPurgedHandler = summary =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var selected = SelectedContact?.UID;
                            if (!string.IsNullOrWhiteSpace(selected))
                            {
                                LoadConversation(selected!);
                            }
                            else
                            {
                                Messages.Clear();
                            }
                        }
                        catch { }

                        try { ShowGlobalPurgeNotice(summary); } catch { }
                    });
                };
                AppServices.Events.AllMessagesPurged += allMessagesPurgedHandler;
                _teardownActions.Add(() => AppServices.Events.AllMessagesPurged -= allMessagesPurgedHandler);
            }
            catch { }
        }

        // [PORT-ALERT] Red toast state for port conflict guidance
        private bool _portAlertVisible;
        public bool PortAlertVisible
        {
            get => _portAlertVisible;
            set
            {
                if (_portAlertVisible != value)
                {
                    _portAlertVisible = value; OnPropertyChanged();
                    try { if (P2PTalk.Utilities.LoggingPaths.Enabled) System.IO.File.AppendAllText(P2PTalk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast][PortAlert] {(value ? "Show" : "Hide")}: '{_portAlertText}'{Environment.NewLine}"); } catch { }
                    // [PORT-ALERT] Auto-hide after a short delay when shown
                    if (value) StartPortAlertAutoHide(TimeSpan.FromSeconds(8));
                }
            }
        }
        private string _portAlertText = string.Empty;
        public string PortAlertText { get => _portAlertText; set { _portAlertText = value; OnPropertyChanged(); } }

        // [PORT-ALERT] Cancellable auto-timeout for the toast
        private CancellationTokenSource? _portAlertCts;
        private void StartPortAlertAutoHide(TimeSpan delay)
        {
            try { _portAlertCts?.Cancel(); } catch { }
            _portAlertCts = new CancellationTokenSource();
            var token = _portAlertCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, token);
                    if (!token.IsCancellationRequested)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!token.IsCancellationRequested) PortAlertVisible = false; });
                    }
                }
                catch { }
            }, token);
        }

        private void ShowGlobalPurgeNotice(MessagePurgeSummary summary)
        {
            var details = summary.MessagesDeleted > 0
                ? $"Purged {summary.MessagesDeleted} messages"
                : "Conversations cleared";

            if (summary.QueuedMessagesDeleted > 0)
            {
                details += $" and removed {summary.QueuedMessagesDeleted} pending messages";
            }

            _ = AppServices.Dialogs.ShowInfoAsync("Messages purged", details, dismissAfterMs: 3000);
        }

        private void RefreshIdentityBindings()
        {
            try
            {
                // Display name updates
                var dn = AppServices.Identity.DisplayName;
                if (!string.IsNullOrWhiteSpace(dn)) LoggedInUsername = dn;
                // Notify UID change so bindings like LoggedInUidShort refresh
                OnPropertyChanged(nameof(LoggedInUidShort));
                // Also notify LoggedInUidFull so name header MultiBinding re-evaluates post-unlock
                OnPropertyChanged(nameof(LoggedInUidFull));
                // Avatar updates
                var bytes = AppServices.Identity.AvatarBytes;
                if (bytes == null || bytes.Length == 0) IdentityAvatarImage = null;
                else { using var ms = new System.IO.MemoryStream(bytes); IdentityAvatarImage = new Avalonia.Media.Imaging.Bitmap(ms); }
            }
            catch { }
        }

        // Display-friendly identity string for headers: prefer DisplayName; show username+UID only in inspection/adding contexts
        public string SelectedContactIdentity
        {
            get
            {
                var c = SelectedContact;
                if (c == null) return string.Empty;
                // Always prefer DisplayName visually; show UID next to it in the header (already bound separately)
                return c.DisplayName;
            }
        }

        public string SelectedContactDisplayName => SelectedContact?.DisplayName ?? string.Empty;

        // Preferred public key for the selected contact: observed (from peers) first, then expected, else placeholder
        public string SelectedContactPublicKeyHex
        {
            get
            {
                try
                {
                    var uid = SelectedContact?.UID;
                    if (string.IsNullOrWhiteSpace(uid)) return "Unknown until connected";
                    var peer = AppServices.Peers.Peers.FirstOrDefault(p => string.Equals(p.UID, uid, StringComparison.OrdinalIgnoreCase));
                    var observed = peer?.PublicKeyHex;
                    if (!string.IsNullOrWhiteSpace(observed)) return observed!;
                    var expected = SelectedContact?.ExpectedPublicKeyHex;
                    if (!string.IsNullOrWhiteSpace(expected)) return expected!;
                    var lastKnown = SelectedContact?.LastKnownPublicKeyHex;
                    if (!string.IsNullOrWhiteSpace(lastKnown)) return lastKnown!;
                    return "Unknown until connected";
                }
                catch { return "Unknown until connected"; }
            }
        }

        // Chat encryption indicator: true if we have an encrypted session with the selected contact
        public bool IsChatEncrypted
        {
            get
            {
                try
                {
                    var uid = SelectedContact?.UID;
                    if (string.IsNullOrWhiteSpace(uid)) return false;
                    if (AppServices.Network.HasEncryptedSession(uid)) return true;
                    // Offline hint: show last-known encrypted if no live session
                    return SelectedContact?.LastKnownEncrypted == true;
                }
                catch { return false; }
            }
        }

        // Full profile UI state
        private bool _isFullProfileOpen;
        public bool IsFullProfileOpen { get => _isFullProfileOpen; set { if (_isFullProfileOpen != value) { _isFullProfileOpen = value; OnPropertyChanged(); } } }

        // Editable fields for simulated contacts
        private string _editableDisplayName = string.Empty;
        public string EditableDisplayName { get => _editableDisplayName; set { if (_editableDisplayName != value) { _editableDisplayName = value; OnPropertyChanged(); (SaveSimulatedProfileCommand as RelayCommand)?.RaiseCanExecuteChanged(); } } }
        public bool IsSelectedContactSimulated => SelectedContact?.IsSimulated == true;

        private bool CanSaveSimulatedProfile()
            => SelectedContact?.IsSimulated == true
               && !string.IsNullOrWhiteSpace(EditableDisplayName)
               && !string.Equals(EditableDisplayName.Trim(), SelectedContact.DisplayName, StringComparison.Ordinal);

        private void SaveSimulatedProfile()
        {
            try
            {
                var c = SelectedContact;
                if (c?.IsSimulated == true)
                {
                    var trimmed = EditableDisplayName.Trim();
                    if (AppServices.Contacts.UpdateDisplayName(c.UID, trimmed, AppServices.Passphrase))
                    {
                        c.DisplayName = trimmed;
                        OnPropertyChanged(nameof(SelectedContact));
                        OnPropertyChanged(nameof(SelectedContactDisplayName));
                        OnPropertyChanged(nameof(SelectedContactIdentity));
                    }
                }
            }
            catch { }
        }

        private int _avatarVersion;
        public int AvatarVersion
        {
            get => _avatarVersion;
            private set
            {
                if (_avatarVersion != value)
                {
                    _avatarVersion = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool CanSend()
        {
            if (SelectedContact == null) return false;
            if (string.IsNullOrWhiteSpace(OutgoingMessage)) return false;
            var uid = AppServices.Identity.UID;
            return !string.IsNullOrWhiteSpace(uid);
        }

        private void SendMessage()
        {
            try
            {
                var contact = SelectedContact;
                if (contact == null) return;

                var content = (OutgoingMessage ?? string.Empty).TrimEnd();
                if (string.IsNullOrWhiteSpace(content)) return;

                var senderUid = TrimUidPrefix(AppServices.Identity.UID ?? string.Empty);
                if (string.IsNullOrWhiteSpace(senderUid)) return;

                var recipientUid = TrimUidPrefix(contact.UID ?? string.Empty);
                if (string.IsNullOrWhiteSpace(recipientUid)) return;

                var now = DateTime.UtcNow;

                var message = new Message
                {
                    Id = Guid.NewGuid(),
                    SenderUID = senderUid,
                    RecipientUID = recipientUid,
                    Content = content,
                    Timestamp = now,
                    DeliveryStatus = "Pending",
                    Signature = Array.Empty<byte>(),
                    SenderPublicKey = Array.Empty<byte>()
                };

                try
                {
                    var payload = System.Text.Encoding.UTF8.GetBytes($"{senderUid}\n{recipientUid}\n{now.Ticks}\n{content}");
                    message.Signature = AppServices.Identity.Sign(payload);
                }
                catch { }

                try
                {
                    var pk = AppServices.Identity.PublicKey;
                    if (pk != null && pk.Length > 0)
                    {
                        message.SenderPublicKey = pk.ToArray();
                    }
                }
                catch { }

                Messages.Add(message);
                try { _messagesStore.StoreMessage(recipientUid, message, AppServices.Passphrase); } catch { }
                OutgoingMessage = string.Empty;

                if (contact.IsSimulated)
                {
                    HandleSimulatedSend(contact, message, content);
                    return;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        Logger.NetworkLog($"SendAttempt: peer={recipientUid} id={message.Id} contentLen={message.Content?.Length ?? 0}");
                        var sent = await AppServices.Network.SendChatAsync(recipientUid, message.Id, content, CancellationToken.None);
                        if (sent)
                        {
                            message.DeliveryStatus = "Sent";
                            message.DeliveredUtc = DateTime.UtcNow;
                            try { _messagesStore.UpdateDelivery(recipientUid, message.Id, message.DeliveryStatus, message.DeliveredUtc, AppServices.Passphrase); } catch { }
                            try { Logger.NetworkLog($"SendResult: Sent | peer={recipientUid} id={message.Id}"); } catch { }
                        }
                        else
                        {
                            try { Logger.NetworkLog($"SendResult: Queued | peer={recipientUid} id={message.Id}"); } catch { }
                            QueueOutgoingMessage(recipientUid, message, "Contact offline. Message queued for delivery.");
                        }
                    }
                    catch (Exception ex)
                    {
                        try { Logger.NetworkLog($"SendResult: Exception | peer={recipientUid} id={message.Id} ex={ex.Message}"); } catch { }
                        QueueOutgoingMessage(recipientUid, message, "Contact offline. Message queued for delivery.");
                    }
                });
            }
            catch { }
        }

        private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                    CancelAllPreviewFetches();
                    ClearTimeline();
                    break;
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems == null) return;
                    var appended = e.NewStartingIndex >= (Messages.Count - e.NewItems.Count);
                    if (!appended)
                    {
                        foreach (var message in e.NewItems.OfType<Message>())
                        {
                            EnsureLinkPreviewProbe(message);
                        }
                        RebuildTimeline();
                        return;
                    }
                    foreach (var message in e.NewItems.OfType<Message>())
                    {
                        AppendMessageToTimeline(message);
                        EnsureLinkPreviewProbe(message);
                    }
                    break;
                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (var removed in e.OldItems.OfType<Message>())
                        {
                            CancelPreviewFetch(removed.Id);
                        }
                    }
                    RebuildTimeline();
                    break;
                case NotifyCollectionChangedAction.Replace:
                    if (e.OldItems != null)
                    {
                        foreach (var removed in e.OldItems.OfType<Message>())
                        {
                            CancelPreviewFetch(removed.Id);
                        }
                    }
                    if (e.NewItems != null)
                    {
                        foreach (var message in e.NewItems.OfType<Message>())
                        {
                            EnsureLinkPreviewProbe(message, forceRefresh: true);
                        }
                    }
                    RebuildTimeline();
                    break;
                case NotifyCollectionChangedAction.Move:
                    RebuildTimeline();
                    break;
            }
        }

        private void ClearTimeline()
        {
            TimelineItems.Clear();
            _lastTimelineDate = null;
        }

        private void RebuildTimeline()
        {
            ClearTimeline();
            foreach (var message in Messages)
            {
                AppendMessageToTimeline(message);
            }
        }

        private void AppendMessageToTimeline(Message message)
        {
            try
            {
                var localDate = NormalizeTimestamp(message.Timestamp).ToLocalTime().Date;
                if (_lastTimelineDate != localDate)
                {
                    TimelineItems.Add(new ChatTimelineDateHeader(localDate));
                    _lastTimelineDate = localDate;
                }
                TimelineItems.Add(message);
            }
            catch { }
        }

        private void EnsureLinkPreviewProbe(Message message, bool forceRefresh = false)
        {
            if (message == null) return;
            if (message.Id == Guid.Empty) return;

            var content = message.Content ?? string.Empty;
            if (!LinkPreviewService.TryExtractFirstUrl(content, out var url))
            {
                CancelPreviewFetch(message.Id);
                if (message.LinkPreview != null)
                {
                    message.LinkPreview = null;
                    PersistLinkPreview(message, null);
                }
                return;
            }

            var existing = message.LinkPreview;
            if (!forceRefresh && existing != null && !existing.IsEmpty &&
                string.Equals(existing.Url, url, StringComparison.OrdinalIgnoreCase) &&
                (DateTime.UtcNow - existing.FetchedUtc) <= LinkPreviewService.PreviewCacheDuration)
            {
                return;
            }

            CancelPreviewFetch(message.Id);

            if (forceRefresh && existing != null && !string.Equals(existing.Url, url, StringComparison.OrdinalIgnoreCase))
            {
                message.LinkPreview = null;
                PersistLinkPreview(message, null);
            }

            StartLinkPreviewFetch(message, url);
        }

        private void StartLinkPreviewFetch(Message message, string url)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            lock (_linkPreviewLock)
            {
                _linkPreviewFetchTokens[message.Id] = cts;
            }

            _ = Task.Run(async () =>
            {
                LinkPreview? preview = null;
                try
                {
                    preview = await AppServices.LinkPreview.GetPreviewAsync(url, cts.Token).ConfigureAwait(false);
                    if (preview == null) return;
                    PersistLinkPreview(message, preview);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        message.LinkPreview = preview;
                    });
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    try { P2PTalk.Utilities.ErrorLogger.LogException(ex, source: "UI.LinkPreviewFetch"); } catch { }
                }
                finally
                {
                    lock (_linkPreviewLock)
                    {
                        if (_linkPreviewFetchTokens.TryGetValue(message.Id, out var existing) && existing == cts)
                        {
                            _linkPreviewFetchTokens.Remove(message.Id);
                        }
                    }
                    cts.Dispose();
                }
            });
        }

        private void CancelPreviewFetch(Guid messageId)
        {
            CancellationTokenSource? cts = null;
            lock (_linkPreviewLock)
            {
                if (_linkPreviewFetchTokens.TryGetValue(messageId, out cts))
                {
                    _linkPreviewFetchTokens.Remove(messageId);
                }
            }
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }

        private void CancelAllPreviewFetches()
        {
            List<CancellationTokenSource> pending;
            lock (_linkPreviewLock)
            {
                pending = _linkPreviewFetchTokens.Values.ToList();
                _linkPreviewFetchTokens.Clear();
            }

            foreach (var cts in pending)
            {
                try { cts.Cancel(); } catch { }
                cts.Dispose();
            }
        }

        private static string GetPeerUidForMessage(Message message)
        {
            var self = TrimUidPrefix(AppServices.Identity.UID ?? string.Empty);
            var sender = TrimUidPrefix(message.SenderUID ?? string.Empty);
            var recipient = TrimUidPrefix(message.RecipientUID ?? string.Empty);
            return string.Equals(sender, self, StringComparison.OrdinalIgnoreCase) ? recipient : sender;
        }

        private void PersistLinkPreview(Message message, LinkPreview? preview)
        {
            try
            {
                var peerUid = GetPeerUidForMessage(message);
                if (string.IsNullOrWhiteSpace(peerUid)) return;
                _messagesStore.UpdateLinkPreview(peerUid, message.Id, preview, AppServices.Passphrase);
            }
            catch { }
        }

        private static DateTime NormalizeTimestamp(DateTime timestamp)
        {
            if (timestamp == default)
                return DateTime.UtcNow;
            if (timestamp.Kind == DateTimeKind.Unspecified)
                return DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
            return timestamp;
        }

        private void QueueOutgoingMessage(string recipientUid, Message message, string banner)
        {
            try { AppServices.Outbox.Enqueue(recipientUid, message, AppServices.Passphrase); } catch { }
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowOfflineBanner(banner));
        }

        private void MarkMessagesAsRead(IEnumerable<Message>? subset = null)
        {
            try
            {
                var contact = SelectedContact;
                if (contact == null) return;
                if (contact.IsSimulated) return;
                var peerUid = TrimUidPrefix(contact.UID ?? string.Empty);
                if (string.IsNullOrWhiteSpace(peerUid)) return;

                var targets = subset ?? Messages.ToList();
                foreach (var message in targets)
                {
                    if (message == null) continue;
                    if (message.ReadUtc.HasValue) continue;
                    if (message.Id == Guid.Empty) continue;
                    var sender = TrimUidPrefix(message.SenderUID ?? string.Empty);
                    if (!string.Equals(sender, peerUid, StringComparison.OrdinalIgnoreCase)) continue;

                    var marked = DateTime.UtcNow;
                    message.ReadUtc = marked;
                    try { _messagesStore.UpdateDelivery(peerUid, message.Id, message.DeliveryStatus, message.DeliveredUtc, AppServices.Passphrase, message.ReadUtc); } catch { }
                    _ = Task.Run(async () =>
                    {
                        try { await AppServices.Network.SendReadReceiptAsync(peerUid, message.Id, CancellationToken.None); } catch { }
                    });
                }
            }
            catch { }
        }

    private void HandleSimulatedSend(Contact contact, Message outbound, string content)
        {
            try
            {
                var recipientUid = TrimUidPrefix(contact.UID ?? string.Empty);
                if (string.IsNullOrWhiteSpace(recipientUid)) return;

                if (contact.Presence == PresenceStatus.Offline)
                {
                    QueueOutgoingMessage(recipientUid, outbound, "Simulated contact is offline. Message will send when they go online.");
                    return;
                }

                // Start with Sending status to show spinner initially
                outbound.DeliveryStatus = "Sending";
                try { _messagesStore.UpdateDelivery(recipientUid, outbound.Id, outbound.DeliveryStatus, null, AppServices.Passphrase); } catch { }
                try { Logger.NetworkLog($"SimSend-Start: peer={recipientUid} id={outbound.Id} status=Sending"); } catch { }

                // Simulate realistic delivery progression: Sending → Sent → Received → Read
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Step 1: Mark as Sent (network accepted)
                        await Task.Delay(100);
                        var sentUtc = DateTime.UtcNow;
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            outbound.DeliveryStatus = "Sent";
                            outbound.DeliveredUtc = sentUtc;
                        });
                        try { _messagesStore.UpdateDelivery(recipientUid, outbound.Id, "Sent", sentUtc, AppServices.Passphrase); } catch { }
                        try { AppServices.Events.RaiseOutboundDeliveryUpdated(recipientUid, outbound.Id, "Sent", sentUtc); } catch { }
                        try { Logger.NetworkLog($"SimSend-Sent: peer={recipientUid} id={outbound.Id} status=Sent"); } catch { }

                        // Step 2: Mark as Read (remote read it) - skipping "Received" status
                        await Task.Delay(1200);
                        var readUtc = DateTime.UtcNow;
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            outbound.DeliveryStatus = "Read";
                            outbound.ReadUtc = readUtc;
                        });
                        try { _messagesStore.UpdateDelivery(recipientUid, outbound.Id, "Read", sentUtc, AppServices.Passphrase, readUtc); } catch { }
                        try { AppServices.Events.RaiseOutboundDeliveryUpdated(recipientUid, outbound.Id, "Read", readUtc); } catch { }
                        try { Logger.NetworkLog($"SimSend-Read: peer={recipientUid} id={outbound.Id} status=Read"); } catch { }
                    }
                    catch { }
                });

                var echo = new Message
                {
                    Id = Guid.NewGuid(),
                    SenderUID = recipientUid,
                    RecipientUID = outbound.SenderUID,
                    Content = content,
                    Timestamp = DateTime.UtcNow,
                    DeliveryStatus = "Received",
                    DeliveredUtc = DateTime.UtcNow,
                    Signature = Array.Empty<byte>(),
                    SenderPublicKey = Array.Empty<byte>(),
                    RelatedMessageId = outbound.Id
                };

                try { _messagesStore.UpdateRelated(recipientUid, outbound.Id, echo.Id, AppServices.Passphrase); } catch { }
                try { _messagesStore.StoreMessage(recipientUid, echo, AppServices.Passphrase); } catch { }

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Messages.Add(echo);
                    OnPropertyChanged(nameof(Messages));
                });
            }
            catch { }
        }

        // Editing permitted for 25 minutes after sending
        private static readonly TimeSpan EditWindow = TimeSpan.FromMinutes(25);

        private bool IsWithinEditWindow(Message msg)
        {
            try
            {
                // Pending messages remain editable until delivery succeeds
                if (string.Equals(msg.DeliveryStatus, "Pending", StringComparison.OrdinalIgnoreCase))
                    return true;

                var start = msg.DeliveredUtc ?? msg.Timestamp;
                if (start == default)
                    return false;

                return (DateTime.UtcNow - start) <= EditWindow;
            }
            catch { return false; }
        }
        private bool IsOwnMessage(Message msg)
        {
            var me = LoggedInUidFull;
            return !string.IsNullOrWhiteSpace(me) && string.Equals(msg.SenderUID, me, StringComparison.OrdinalIgnoreCase);
        }
        public bool CanEditMessage(object? _)
        {
            // Used by XAML binding (not parameter-aware). Conservatively return true and enforce per-message checks in command.
            return true;
        }

        // Per-message edit enabling for XAML converters
    public bool CanEditMessage(Message m)
        {
            try
            {
                if (!IsOwnMessage(m)) return false;
                if (string.Equals(m.DeliveryStatus, "Pending", StringComparison.OrdinalIgnoreCase)) return true;
                return IsWithinEditWindow(m);
            }
            catch { return false; }
        }

        // Drain pending messages for a simulated contact when they come online
        public void DrainSimulatedPending(string uid)
        {
            try
            {
                var targetUid = string.IsNullOrWhiteSpace(uid) ? (SelectedContact?.UID ?? string.Empty) : uid;
                if (string.IsNullOrWhiteSpace(targetUid)) return;

                var target = Contacts.FirstOrDefault(c => string.Equals(c.UID, targetUid, StringComparison.OrdinalIgnoreCase));
                if (target?.IsSimulated != true) return;

                StartOfflineBannerFadeOut();

                var me = LoggedInUidFull ?? string.Empty;
                var now = DateTime.UtcNow;
                var pendings = Messages.Where(m => string.Equals(m.SenderUID, me, StringComparison.OrdinalIgnoreCase)
                                                && string.Equals(m.RecipientUID, targetUid, StringComparison.OrdinalIgnoreCase)
                                                && string.Equals(m.DeliveryStatus, "Pending", StringComparison.OrdinalIgnoreCase))
                                       .ToList();
                foreach (var msg in pendings)
                {
                    msg.DeliveryStatus = "Sent"; // delivered to simulated contact
                    msg.DeliveredUtc = now;
                    try { _messagesStore.UpdateDelivery(targetUid, msg.Id, "Sent", now, AppServices.Passphrase); } catch { }

                    var echo = new Message
                    {
                        Id = Guid.NewGuid(),
                        SenderUID = targetUid,
                        RecipientUID = me,
                        Content = msg.Content,
                        Timestamp = now,
                        Signature = Array.Empty<byte>(),
                        SenderPublicKey = Array.Empty<byte>(),
                        RelatedMessageId = msg.Id
                    };

                    try { _messagesStore.UpdateRelated(targetUid, msg.Id, echo.Id, AppServices.Passphrase); } catch { }
                    Messages.Add(echo);
                    try { _messagesStore.StoreMessage(targetUid, echo, AppServices.Passphrase); } catch { }
                    try { AppServices.OutboxCancelIfQueued(targetUid, msg.Id, AppServices.Passphrase); } catch { }
                }
            }
            catch { }
        }

        private string _lastNatState = "Unknown";
        private DateTime _lastNatStateAt = DateTime.MinValue;
        private DateTime _lastNatNatComputedAt = DateTime.MinValue;

        public string? NatStatus => AppServices.Nat?.Status;

        public string NatSimpleStatus => ComputeNatSimpleStatus();

        public string? NatVerification => AppServices.Nat?.MappingVerification;

        private string ComputeNatSimpleStatus()
        {
            try
            {
                var status = (AppServices.Nat?.Status ?? string.Empty).ToLowerInvariant();
                var verification = (AppServices.Nat?.MappingVerification ?? string.Empty).ToLowerInvariant();
                var now = DateTime.UtcNow;

                string current = status switch
                {
                    var s when s.Contains("discovering") => "Discovering",
                    var s when s.Contains("mapping") => "Mapping",
                    var s when s.Contains("verified") => "Mapped",
                    var s when s.Contains("mapped") && !s.Contains("unmapped") => "Mapped",
                    var s when s.Contains("no gateway") => "No gateway",
                    var s when s.Contains("unmapped") => "Unmapped",
                    var s when s.Contains("failed") || s.Contains("error") => "Failed",
                    _ => "Unknown"
                };

                if (verification.Contains("reachable") || verification.Contains("ok")) current = "Mapped";
                else if (verification.Contains("unmapped")) current = "Unmapped";
                else if (verification.Contains("failed") || verification.Contains("unreachable")) current = "Failed";

                if (!string.Equals(_lastNatState, current, StringComparison.Ordinal))
                {
                    if ((now - _lastNatNatComputedAt) < TimeSpan.FromSeconds(7))
                    {
                        return _lastNatState;
                    }

                    _lastNatState = current;
                    _lastNatStateAt = now;
                }

                _lastNatNatComputedAt = now;
                return _lastNatState;
            }
            catch { return _lastNatState; }
        }
        // [PRIVACY] Sanitized verification string when sensitive info is hidden (IPs/ports obfuscated).
        public string NatVerificationSafe
        {
            get
            {
                var raw = NatVerification ?? string.Empty;
                if (DiagnosticsSensitive) return raw;
                // Replace IPv4 addresses
                var noIp = Regex.Replace(raw, "\\b(25[0-5]|2[0-4]\\d|[0-1]?\\d?\\d)(\\.(25[0-5]|2[0-4]\\d|[0-1]?\\d?\\d)){3}\\b", "••.••.••.••");
                // Replace :port patterns
                var noPort = Regex.Replace(noIp, @":\\d{2,5}", ":•••");
                // General long numbers (e.g., ports without colon or IDs)
                var noNums = Regex.Replace(noPort, "\\b\\d{2,5}\\b", "••••");
                return noNums;
            }
        }
        // [PRIVACY] Diagnostics panel: obfuscate ports when sensitive info is hidden
    private bool _diagnosticsSensitive; // default hidden
        public bool DiagnosticsSensitive { get => _diagnosticsSensitive; set { if (_diagnosticsSensitive != value) { _diagnosticsSensitive = value; OnPropertyChanged(); NotifyDiagnostics(); } } }
    private static string ObfuscateNumber(int n) => new string('•', Math.Min(5, n.ToString(CultureInfo.InvariantCulture).Length));
        public string TcpPortLabel
        {
            get
            {
                if (AppServices.Network.ListeningPort is int p)
                    return DiagnosticsSensitive ? $"TCP: {p}" : $"TCP: {ObfuscateNumber(p)}";
                return "TCP: n/a";
            }
        }
        public string UdpPortLabel
        {
            get
            {
                if (AppServices.Network.UdpBoundPort is int p)
                    return DiagnosticsSensitive ? $"UDP: {p}" : $"UDP: {ObfuscateNumber(p)}";
                return "UDP: n/a";
            }
        }
        public string ExternalPortLabel
        {
            get
            {
                if (AppServices.Nat.MappedTcpPort is int tp && AppServices.Nat.MappedUdpPort is int up)
                    return DiagnosticsSensitive ? $"External: {tp} → {up}" : $"External: {ObfuscateNumber(tp)} → {ObfuscateNumber(up)}";
                return "External: n/a";
            }
        }

        // Inline offline banner state (chat header)
        private bool _offlineBannerVisible;
        public bool OfflineBannerVisible { get => _offlineBannerVisible; set { if (_offlineBannerVisible != value) { _offlineBannerVisible = value; OnPropertyChanged(); } } }
        private string _offlineBannerText = string.Empty;
        public string OfflineBannerText { get => _offlineBannerText; set { if (_offlineBannerText != value) { _offlineBannerText = value; OnPropertyChanged(); } } }
        private double _offlineBannerOpacity = 1.0;
        public double OfflineBannerOpacity { get => _offlineBannerOpacity; set { if (Math.Abs(_offlineBannerOpacity - value) > 0.0001) { _offlineBannerOpacity = value; OnPropertyChanged(); } } }
        private void ShowOfflineBanner(string text)
        {
            try
            {
                OfflineBannerText = text;
                OfflineBannerOpacity = 1.0;
                OfflineBannerVisible = true;
            }
            catch { }
        }
        private void HideOfflineBanner()
        {
            try { OfflineBannerVisible = false; } catch { }
        }
        private void StartOfflineBannerFadeOut()
        {
            try
            {
                if (!OfflineBannerVisible) return;
                OfflineBannerOpacity = 0.0;
                var cts = new CancellationTokenSource();
                var token = cts.Token;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(220), token);
                        if (!token.IsCancellationRequested)
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                OfflineBannerVisible = false;
                                OfflineBannerOpacity = 1.0; // reset for next time
                            });
                        }
                    }
                    catch { }
                }, token);
            }
            catch { }
        }

        private IBrush _natIndicatorBrush = Brushes.Gray;
        public IBrush NatIndicatorBrush { get => _natIndicatorBrush; set { _natIndicatorBrush = value; OnPropertyChanged(); } }
        private double _natIndicatorOpacity = 1.0;
        public double NatIndicatorOpacity { get => _natIndicatorOpacity; set { _natIndicatorOpacity = value; OnPropertyChanged(); } }
        public bool NatIndicatorBlink { get; private set; }

        // Ensure UI-thread notifications when called from event handlers
        public void NotifyDiagnostics()
        {
            // [STATUS] Notify compact status and sanitized text
            OnPropertyChanged(nameof(NatStatus)); // keep for any dependent logic
            OnPropertyChanged(nameof(NatSimpleStatus));
            OnPropertyChanged(nameof(NatVerification));
            OnPropertyChanged(nameof(NatVerificationSafe));
            OnPropertyChanged(nameof(TcpPortLabel));
            OnPropertyChanged(nameof(UdpPortLabel));
            OnPropertyChanged(nameof(ExternalPortLabel));
            EvaluateNatIndicator();
        }

        private void EvaluateNatIndicator()
        {
            try
            {
                var s = (NatStatus ?? string.Empty).ToLowerInvariant();
                var v = (NatVerification ?? string.Empty).ToLowerInvariant();
                if (s.Contains("discovering") || (s.Contains("gateway discovered") && string.IsNullOrWhiteSpace(v)))
                { NatIndicatorBrush = Brushes.Goldenrod; NatIndicatorBlink = true; if (NatIndicatorOpacity <= 0) NatIndicatorOpacity = 1.0; return; }
                // Explicitly treat "unmapped" and "no gateway" as neutral gray (not an error)
                if (s.Contains("unmapped") || v.Contains("unmapped") || s.Contains("no gateway"))
                { NatIndicatorBrush = Brushes.Gray; NatIndicatorBlink = false; NatIndicatorOpacity = 1.0; return; }
                if (s.Contains("failed") || v.Contains("unreachable") || v.Contains("failed"))
                { NatIndicatorBrush = Brushes.IndianRed; NatIndicatorBlink = false; NatIndicatorOpacity = 1.0; return; }
                if (v.Contains("reachable") || v.Contains("ok") || (s.Contains("mapped") && !s.Contains("unmapped")))
                { NatIndicatorBrush = Brushes.LimeGreen; NatIndicatorBlink = false; NatIndicatorOpacity = 1.0; return; }
                NatIndicatorBrush = Brushes.Gray; NatIndicatorBlink = false; NatIndicatorOpacity = 1.0;
            }
            catch { }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // Public helper for code-behind (e.g., Debug-only UI) to refresh bindings that depend on SelectedContact
        public void RaiseSelectedContactChanged()
        {
            OnPropertyChanged(nameof(SelectedContact));
            OnPropertyChanged(nameof(SelectedContactIdentity));
        }

        // Group headers removed; UI binds directly to Contacts

        private static bool IsUid(string s)
        {
            // New policy: no 'usr-' prefix and no dashes allowed. Alphanumeric only, length >= 8.
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s.StartsWith("usr-", StringComparison.Ordinal)) return false;
            if (s.Contains('-')) return false;
            if (s.Length < 8) return false;
            foreach (var ch in s)
            {
                if (!char.IsLetterOrDigit(ch)) return false;
            }
            return true;
        }

        // Helper: compact identity string shown where space is limited
        private static string CompactIdentity(string usernamePlusUid)
        {
            if (string.IsNullOrWhiteSpace(usernamePlusUid)) return string.Empty;
            // Expect pattern name-usr-XXXX...; if too long, return just usr-XXXX...; never return raw username alone
            var idx = usernamePlusUid.LastIndexOf('-');
            if (idx > 0 && idx < usernamePlusUid.Length - 1)
            {
                var uid = usernamePlusUid[(idx + 1)..];
                if (IsUid(uid)) return uid.Length > 12 ? uid[..12] : uid; // short visual form when needed
            }
            return usernamePlusUid;
        }

        private static string TrimUidPrefix(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
            return uid.StartsWith("usr-", StringComparison.Ordinal) && uid.Length > 4 ? uid.Substring(4) : uid;
        }

        // Helper: format a remaining timespan as mm:ss
        private static string FormatMmSs(TimeSpan ts)
        {
            if (ts <= TimeSpan.Zero) return "0:00";
            var total = (int)Math.Ceiling(ts.TotalSeconds);
            var m = total / 60;
            var s = total % 60;
            return $"{m}:{s:00}";
        }

        // These helpers are used by XAML via MultiBinding with UiTick to update each pulse.
        public string GetEditRemaining(Message m)
        {
            try
            {
                // No edit timer for Pending messages
                if (string.Equals(m.DeliveryStatus, "Pending", StringComparison.OrdinalIgnoreCase)) return string.Empty;

                var start = m.DeliveredUtc ?? m.Timestamp;
                if (start == default) return string.Empty;

                var now = DateTime.UtcNow;
                var elapsed = now - start;
                var remaining = EditWindow - elapsed;
                if (remaining <= TimeSpan.Zero) return "Edit expired";
                return $"Edit {FormatMmSs(remaining)}";
            }
            catch { return string.Empty; }
        }

        public bool ShowEditRemaining(Message m)
        {
            try
            {
                // Hide countdown for Pending messages
                if (string.Equals(m.DeliveryStatus, "Pending", StringComparison.OrdinalIgnoreCase)) return false;
                return IsOwnMessage(m);
            }
            catch { return false; }
        }

        private bool CanAddContact()
        {
            if (string.IsNullOrWhiteSpace(AddContactInput)) return false;
            var input = AddContactInput.Trim();
            // Accept UID only, or name-UID
            if (IsUid(input)) return true;
            var idx = input.LastIndexOf('-');
            if (idx > 0 && idx < input.Length - 1)
            {
                var uid = input[(idx + 1)..];
                return IsUid(uid);
            }
            return false;
        }

        private void AddContact()
        {
            ErrorMessage = string.Empty;
            var input = AddContactInput?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(input)) { ErrorMessage = "Enter a UID or username-UID (e.g., bob-45K5w52g)."; return; }
            string uid;
            string displayName;
            if (IsUid(input))
            {
                uid = TrimUidPrefix(input);
                displayName = uid;
            }
            else
            {
                var idx = input.LastIndexOf('-');
                if (idx <= 0 || idx >= input.Length - 1)
                {
                    ErrorMessage = "Enter UID or username-UID (e.g., bob-45K5w52g).";
                    return;
                }
                uid = input[(idx + 1)..];
                if (!IsUid(uid))
                {
                    ErrorMessage = "Invalid UID. Expected 8+ alphanumeric characters.";
                    return;
                }
                displayName = input[..idx];
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    ErrorMessage = "Username required with UID or provide UID alone.";
                    return;
                }
            }

            var added = AppServices.Contacts.AddContact(new Contact { UID = uid, DisplayName = displayName }, AppServices.Passphrase);
            if (!added)
            {
                ErrorMessage = "Contact already exists or invalid.";
                return;
            }
            AppServices.Peers.IncludeContacts();
            AddContactInput = string.Empty;
        }

        private void LoadConversation(string peerUid)
        {
            try
            {
                peerUid = TrimUidPrefix(peerUid ?? string.Empty);
                Messages.Clear();
                var list = _messagesStore.LoadMessages(peerUid, AppServices.Passphrase);
                foreach (var m in list) Messages.Add(m);
                MarkMessagesAsRead();
            }
            catch { }
        }

        private async Task BurnConversationAsync()
        {
            var contact = SelectedContact;
            if (contact == null) return;

            var peerUid = TrimUidPrefix(contact.UID ?? string.Empty);
            if (string.IsNullOrWhiteSpace(peerUid))
            {
                ErrorMessage = "Unable to resolve contact UID for burn.";
                return;
            }

            var display = string.IsNullOrWhiteSpace(contact.DisplayName) ? peerUid : contact.DisplayName;
            var confirmationText = $"Burn conversation with {display}?\n\n" +
                                   "This process performs irreversible destruction:\n" +
                                   "• Overwrites stored messages with pseudorandom 0/1 patterns\n" +
                                   "• Rewrites the archive with randomized lorem gibberish\n" +
                                   "• Applies an alternating 1/0 sweep followed by an all-zero pass\n" +
                                   "• Deletes residual message and outbox files\n\n" +
                                   "Once completed, the conversation cannot be recovered.";

            bool confirmed;
            try
            {
                confirmed = await AppServices.Dialogs.ConfirmAsync("Burn conversation", confirmationText, "Burn Now", "Cancel");
            }
            catch
            {
                return;
            }

            if (!confirmed) return;

            try
            {
                Messages.Clear();
                ErrorMessage = string.Empty;

                var summary = AppServices.Retention.BurnConversationSecurely(peerUid, AppServices.Passphrase);
                LoadConversation(peerUid);

                var toast = summary.BytesWiped > 0
                    ? $"Secure shred complete ({summary.BytesWiped:N0} bytes wiped)."
                    : "Conversation artifacts were already absent.";
                await AppServices.Dialogs.ShowInfoAsync("Conversation burned", toast, dismissAfterMs: 3500);
            }
            catch (Exception ex)
            {
                ErrorMessage = "Burn failed. Check logs for details.";
                try { P2PTalk.Utilities.ErrorLogger.LogException(ex, source: "UI.BurnConversation"); } catch { }
            }
        }

        private static void CancelCts(ref CancellationTokenSource? cts)
        {
            var current = Interlocked.Exchange(ref cts, null);
            if (current == null) return;
            try { current.Cancel(); } catch { }
            current.Dispose();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || _disposed) return;
            _disposed = true;

            foreach (var teardown in _teardownActions.AsEnumerable().Reverse())
            {
                try { teardown(); } catch { }
            }
            _teardownActions.Clear();

            CancelAllPreviewFetches();
            CancelCts(ref _contactsRefreshCts);
            CancelCts(ref _portAlertCts);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
