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

using Zer0Talk.Containers;
using Zer0Talk.Models;
using Models = Zer0Talk.Models;
using Zer0Talk.Services;
using Zer0Talk.Utilities;

namespace Zer0Talk.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        // Expose a debug-only UI flag for XAML visibility bindings
        public bool IsDebugUi
        {
            get => Zer0Talk.Utilities.RuntimeFlags.ShowDebugUi;
        }

        public string PrototypeBadgeText => Zer0Talk.AppInfo.PrototypeBadgeText;

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

    // --- Status Bar ---
    private int _connectedPeerCount;
    public int ConnectedPeerCount
    {
        get => _connectedPeerCount;
        set { if (_connectedPeerCount != value) { _connectedPeerCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectedPeerCountDisplay)); } }
    }
    public string ConnectedPeerCountDisplay => _connectedPeerCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private bool _isStreamerMode;
    public bool IsStreamerMode
    {
        get => _isStreamerMode;
        set
        {
            if (_isStreamerMode != value)
            {
                _isStreamerMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StreamerModeBadgeVisible));
                OnPropertyChanged(nameof(SelectedContactIdentity));
            }
        }
    }
    public bool StreamerModeBadgeVisible => _isStreamerMode;

    public string LocalizedConnectedPeers => Services.AppServices.Localization.GetString("StatusBar.ConnectedPeers", "Connected Peers");
    public string LocalizedStreamerMode => Services.AppServices.Localization.GetString("StatusBar.StreamerMode", "Streamer Mode");
    public string LocalizedStreamerModeActive => Services.AppServices.Localization.GetString("StatusBar.StreamerModeActive", "STREAMER");

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
                if (Zer0Talk.Utilities.LoggingPaths.Enabled)
                {
                    var line = $"{DateTime.Now:O} [UI] Freeze begin: reason=context/hover count={v}";
                    System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.UI, line + Environment.NewLine);
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
                    if (Zer0Talk.Utilities.LoggingPaths.Enabled)
                    {
                        var line = $"{DateTime.Now:O} [UI] Freeze end";
                        System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.UI, line + Environment.NewLine);
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
                                    {
                                        var previousPresence = existing.Presence;
                                        existing.Presence = kv.Value.Presence;
                                        if (kv.Value.Presence == PresenceStatus.Online && previousPresence != PresenceStatus.Online)
                                        {
                                            HandleContactCameOnline(existing.UID);
                                        }
                                    }
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
                                if (Zer0Talk.Utilities.LoggingPaths.Enabled)
                                {
                                    var frozen = IsSelectionFrozen ? "true" : "false";
                                    var preserved = (!string.IsNullOrWhiteSpace(prevUid) && string.Equals(prevUid, SelectedContact?.UID, StringComparison.OrdinalIgnoreCase)) ? "true" : "false";
                                    var line = $"{DateTime.Now:O} [UI] Contacts refresh (debounced): items={Contacts.Count} selPrev={prevUid ?? "none"} selNow={SelectedContact?.UID ?? "none"} preserved={preserved} frozen={frozen}";
                                    System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.UI, line + Environment.NewLine);
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
        private System.ComponentModel.PropertyChangedEventHandler? _selectedContactPresenceHandler;
        public Contact? SelectedContact
        {
            get => _selectedContact;
            set
            {
                if (_selectedContact != value)
                {
                    // Unsubscribe from previous contact's presence changes
                    if (_selectedContact != null && _selectedContactPresenceHandler != null)
                    {
                        _selectedContact.PropertyChanged -= _selectedContactPresenceHandler;
                    }
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
                        // Show proactive presence banner
                        UpdatePresenceBanner(_selectedContact);
                        // Subscribe to presence changes on this contact
                        _selectedContactPresenceHandler = (s, e) =>
                        {
                            if (e.PropertyName == nameof(Contact.Presence))
                            {
                                Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdatePresenceBanner(_selectedContact));
                            }
                        };
                        _selectedContact.PropertyChanged += _selectedContactPresenceHandler;
                    }
                    else
                    {
                        _selectedContactPresenceHandler = null;
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
    public ICommand? TestInfoToastCommand { get; }
    public ICommand? TestWarningToastCommand { get; }
    public ICommand? TestErrorToastCommand { get; }
    public ICommand? TestMessageToastCommand { get; }

        private bool _hasPendingInvites;
        public bool HasPendingInvites { get => _hasPendingInvites; private set { if (_hasPendingInvites != value) { _hasPendingInvites = value; OnPropertyChanged(); } } }

        // Aggregated notification badge (invites + notices) shown in nav rail
        private int _notificationCount;
        public int NotificationCount
        {
            get => _notificationCount;
            private set
            {
                if (_notificationCount != value)
                {
                    _notificationCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(NotificationBadgeText));
                    OnPropertyChanged(nameof(NotificationBadgeVisible));
                }
            }
        }

        public string NotificationBadgeText
        {
            get
            {
                if (NotificationCount <= 0) return string.Empty;
                return NotificationCount > 99 ? "99+" : NotificationCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        public bool NotificationBadgeVisible => NotificationCount > 0;

        // Optimistic-cleared origins: when the user clears/rejects invites we immediately hide them
        // from the badge until the authoritative services reconcile. Stored as trimmed UIDs.
        private readonly HashSet<string> _optimisticClearedOrigins = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _optimisticClearedLock = new();

        // Live avatar image from IdentityService
        private Avalonia.Media.IImage? _identityAvatarImage;
        public Avalonia.Media.IImage? IdentityAvatarImage { get => _identityAvatarImage; private set { _identityAvatarImage = value; OnPropertyChanged(); } }

        private string _addContactInput = string.Empty;
        public string AddContactInput { get => _addContactInput; set { _addContactInput = value; OnPropertyChanged(); (AddContactCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }
        private string _errorMessage = string.Empty;
        public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }

        // Toggle to enable the new Markdig-based renderer (enabled: true)
        private bool _useMarkdig = true;
        public bool UseMarkdig
        {
            get => _useMarkdig;
            set { if (_useMarkdig != value) { _useMarkdig = value; OnPropertyChanged(); } }
        }

        public MainWindowViewModel()
        {
            RefreshLoggedInUsername();
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

            // Notification aggregation: subscribe to pending invites and notices
            try
            {
                Action pendingChanged = () => { Avalonia.Threading.Dispatcher.UIThread.Post(() => { RefreshHasPendingInvites(); }); };
                AppServices.ContactRequests.PendingChanged += pendingChanged;
                _teardownActions.Add(() => AppServices.ContactRequests.PendingChanged -= pendingChanged);
            }
            catch { }
            try
            {
                Action noticesChanged = () => { Avalonia.Threading.Dispatcher.UIThread.Post(() => { RefreshHasPendingInvites(); }); };
                AppServices.Notifications.NoticesChanged += noticesChanged;
                _teardownActions.Add(() => AppServices.Notifications.NoticesChanged -= noticesChanged);
            }
            catch { }
            try
            {
                Action securityEventsChanged = () => { Avalonia.Threading.Dispatcher.UIThread.Post(() => { RefreshHasPendingInvites(); }); };
                AppServices.Notifications.SecurityEventsChanged += securityEventsChanged;
                _teardownActions.Add(() => AppServices.Notifications.SecurityEventsChanged -= securityEventsChanged);
            }
            catch { }

            // Ensure initial badge state
            RefreshHasPendingInvites();

            // Subscribe to language changes for localized strings
            try
            {
                Action languageChangedHandler = () => { Avalonia.Threading.Dispatcher.UIThread.Post(RefreshLocalizedStrings); };
                AppServices.Localization.LanguageChanged += languageChangedHandler;
                _teardownActions.Add(() => AppServices.Localization.LanguageChanged -= languageChangedHandler);
            }
            catch { }

            // Status bar: subscribe to active session count changes
            try
            {
                ConnectedPeerCount = AppServices.Network.ActiveSessionCount;
                Action<int> sessionCountHandler = count => { Avalonia.Threading.Dispatcher.UIThread.Post(() => ConnectedPeerCount = count); };
                AppServices.Network.SessionCountChanged += sessionCountHandler;
                _teardownActions.Add(() => AppServices.Network.SessionCountChanged -= sessionCountHandler);
            }
            catch { }

            // Load streamer mode from settings
            try { IsStreamerMode = AppServices.Settings.Settings.StreamerMode; } catch { }

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
            ClearInvitesCommand = new RelayCommand(_ =>
            {
                try
                {
                    // Capture current pending inbound requests and their origins
                    var pending = AppServices.ContactRequests.PendingInboundRequests.ToList();
                    var rawOrigins = pending.Select(p => p.Uid).Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
                    var origins = rawOrigins.SelectMany(o => ExpandOriginForms(o)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    // Optimistically mark these origins as cleared so UI updates immediately
                    lock (_optimisticClearedLock)
                    {
                        foreach (var o in origins) _optimisticClearedOrigins.Add(o);
                    }
                    // Clear UI and service pending list immediately
                    try { AppServices.ContactRequests.ClearAllPendingInbound(); } catch { }
                    // Remove related notices so aggregated badge reflects optimistic clear
                    try { AppServices.Notifications.RemoveNoticesForOrigins(origins); } catch { }
                    // Refresh local flags
                    RefreshHasPendingInvites();
                }
                catch { }
            });
#if DEBUG
            TestInfoToastCommand = new RelayCommand(_ => { try { AppServices.Notifications.PostNotice(Models.NotificationType.Information, "This is a test information notification with some longer text to see how it wraps.", isPersistent: false); } catch { } });
            TestWarningToastCommand = new RelayCommand(_ => { try { AppServices.Notifications.PostNotice(Models.NotificationType.Warning, "This is a test warning notification that might indicate something needs attention.", isPersistent: false); } catch { } });
            TestErrorToastCommand = new RelayCommand(_ => { try { AppServices.Notifications.PostNotice(Models.NotificationType.Error, "This is a test error notification showing that something went wrong.", isPersistent: false); } catch { } });
            TestMessageToastCommand = new RelayCommand(_ => { 
                try { 
                    // Test the actual incoming message notification system
                    AppServices.Notifications.AddOrUpdateMessageNotice("Alice", "Hey, are you available for a quick call? This is a test notification with a longer message that should be truncated in the preview.", "alice123", Guid.NewGuid(), incoming: true, DateTime.UtcNow, isUnread: true); 
                } catch (Exception ex) { 
                    System.Diagnostics.Debug.WriteLine($"TESTING: Notification test failed: {ex.Message}");
                } 
            });
#endif

            // Aggregation helpers: compute counts and manage optimistic clears
            // (kept as instance methods so other UI code can call them directly)
            // See RefreshHasPendingInvites() implementation below.

        

            EditMessageCommand = new RelayCommand(async p =>
            {
                try
                {
                    if (SelectedContact == null) return;
                    if (p is not Guid id) return;
                    var msg = Messages.FirstOrDefault(m => m.Id == id);
                    if (msg == null) return;
                    if (!IsOwnMessage(msg)) return; // gate editing to own messages
                    
                    if (!IsWithinEditWindow(msg)) 
                    { 
                        await AppServices.Dialogs.ShowWarningAsync("Edit Time Expired", "That message can't be edited anymore because the 25-minute window has passed."); 
                        return; 
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
                        await AppServices.Dialogs.ShowErrorAsync("Message Deleted", "Sorry but this message was deleted while you were editing it, it will be purged as soon as you finish editing it.");
                        return;
                    }
                    
                    if (!IsWithinEditWindow(msg))
                    {
                        await AppServices.Dialogs.ShowInfoAsync("Edit Time Expired", "That message can't be edited anymore because the 25-minute window has passed.");
                        return;
                    }
                    var peerUid = SelectedContact.UID;
                    msg.Content = newContent;
                    msg.IsEdited = true;
                    msg.EditedUtc = DateTime.UtcNow;
                    EnsureLinkPreviewProbe(msg, forceRefresh: true);
                    OnPropertyChanged(nameof(Messages)); // ensure bindings refresh
                    _messagesStore.UpdateMessage(peerUid, id, newContent, AppServices.Passphrase);
                    // Raise event so other parts of the UI can react if needed
                    AppServices.Events.RaiseMessageEdited(peerUid, id, newContent);
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
                        ? "Are you sure you want to delete this message? This will permanently delete it for both you and the recipient. This action cannot be undone."
                        : "Delete this message locally? The sender will not be notified. This action cannot be undone.";
                    var ok = await AppServices.Dialogs.ConfirmDestructiveAsync("Delete Message", prompt, "Delete", "Cancel");
                    if (!ok) return;
                    // Remove from UI first (user-deleted should be treated as truly deleted)
                    Messages.Remove(msg);
                    _messagesStore.DeleteMessage(SelectedContact.UID, id, AppServices.Passphrase);
                    // Raise event so other parts of the UI can react if needed
                    AppServices.Events.RaiseMessageDeleted(SelectedContact.UID, id);
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
                        await AppServices.Dialogs.ShowCenteredToastAsync("Message Deleted");
                    }
                    catch { }
                }
                catch { }
            });

            void LogManualDelete(string peerUid, Guid messageId, string reason)
            {
                try
                {
                    if (!Zer0Talk.Utilities.LoggingPaths.Enabled) return;
                    var line = $"[RETENTION] {DateTime.Now:O}: User delete {reason} peer={peerUid} id={messageId}";
                    System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.Retention, line + Environment.NewLine);
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
                        Signature = Array.Empty<byte>(),
                        SenderPublicKey = Array.Empty<byte>(),
                    };
                    var selectedUid = SelectedContact?.UID ?? string.Empty;
                    var display = ResolveContactDisplayName(senderUid);
                    var isSelfOnline = IsSelfOnline();
                    if (string.Equals(TrimUidPrefix(selectedUid), senderUid, StringComparison.OrdinalIgnoreCase))
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => Messages.Add(msg));
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            OnPropertyChanged(nameof(IsChatEncrypted));
                            if (isSelfOnline)
                            {
                                MarkMessagesAsRead(new[] { msg });
                            }
                        });
                        // FIXED: Always publish notification for selected conversation too
                        // Audio notifications are handled by presence mode logic, desktop toasts by window state
                        PublishMessageNotification(senderUid, display, msg, incoming: true, unread: true);
                    }
                    else
                    {
                        PublishMessageNotification(senderUid, display, msg, incoming: true, unread: true);
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
            // Received ACKs handler removed - delivery tracking no longer used
            // OutboundDeliveryUpdated handler removed - delivery tracking no longer used
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

                // Handle OpenConversationRequested event to navigate to a specific contact
                Action<string> openConversationHandler = (uid) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(uid)) return;
                            // Find the contact in the list
                            var contact = AppServices.Contacts.Contacts.FirstOrDefault(c => string.Equals(c.UID, uid, StringComparison.OrdinalIgnoreCase));
                            if (contact != null)
                            {
                                // Set as selected contact (this triggers LoadConversation)
                                SelectedContact = contact;
                            }
                        }
                        catch { }
                    });
                };
                AppServices.Events.OpenConversationRequested += openConversationHandler;
                _teardownActions.Add(() => AppServices.Events.OpenConversationRequested -= openConversationHandler);

                // Handle MessageEdited event to refresh UI when message content changes
                Action<string, Guid, string> messageEditedHandler = (peerUid, messageId, newContent) =>
                {
                    if (SelectedContact?.UID != peerUid) return;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var m = Messages.FirstOrDefault(x => x.Id == messageId);
                            if (m != null)
                            {
                                m.Content = newContent;
                                m.IsEdited = true;
                                m.EditedUtc = DateTime.UtcNow;
                                OnPropertyChanged(nameof(Messages));
                            }
                        }
                        catch { }
                    });
                };
                AppServices.Events.MessageEdited += messageEditedHandler;
                _teardownActions.Add(() => AppServices.Events.MessageEdited -= messageEditedHandler);

                // Handle MessageDeleted event to remove message from UI
                Action<string, Guid> messageDeletedHandler = (peerUid, messageId) =>
                {
                    if (SelectedContact?.UID != peerUid) return;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            var m = Messages.FirstOrDefault(x => x.Id == messageId);
                            if (m != null)
                            {
                                Messages.Remove(m);
                            }
                        }
                        catch { }
                    });
                };
                AppServices.Events.MessageDeleted += messageDeletedHandler;
                _teardownActions.Add(() => AppServices.Events.MessageDeleted -= messageDeletedHandler);
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
                    try { if (Zer0Talk.Utilities.LoggingPaths.Enabled) System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Toast][PortAlert] {(value ? "Show" : "Hide")}: '{_portAlertText}'{Environment.NewLine}"); } catch { }
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

            _ = AppServices.Dialogs.ShowSuccessAsync("Messages purged", details, 3000);
        }

    private void RefreshIdentityBindings()
        {
            try
            {
        RefreshLoggedInUsername();
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

        private void RefreshLoggedInUsername()
        {
            try
            {
                var identity = AppServices.Identity;
                var settings = AppServices.Settings.Settings;
                string? label = identity.DisplayName;
                if (string.IsNullOrWhiteSpace(label)) label = settings?.DisplayName;
                if (string.IsNullOrWhiteSpace(label)) label = identity.Username;
                if (string.IsNullOrWhiteSpace(label)) label = identity.UID;
                if (string.IsNullOrWhiteSpace(label)) label = "User";
                LoggedInUsername = label;
            }
            catch { LoggedInUsername = "User"; }
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

        // Settings proxy for UI bindings
        public bool ShowPublicKeys => AppServices.Settings?.Settings?.ShowPublicKeys ?? false;

        /// <summary>
        /// Called when settings are saved to refresh properties that depend on AppSettings.
        /// </summary>
        public void RefreshSettingsDependentProperties()
        {
            OnPropertyChanged(nameof(ShowPublicKeys));
        }

        // Full profile UI state
        private bool _isFullProfileOpen;
        public bool IsFullProfileOpen { get => _isFullProfileOpen; set { if (_isFullProfileOpen != value) { _isFullProfileOpen = value; OnPropertyChanged(); } } }

        // Selected previous name for contact profile dropdown
        private DisplayNameRecord? _selectedContactPreviousName;
        public DisplayNameRecord? SelectedContactPreviousName { get => _selectedContactPreviousName; set { if (_selectedContactPreviousName != value) { _selectedContactPreviousName = value; OnPropertyChanged(); } } }

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

                // Play outgoing message sound
                try
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Services.AudioHelper.PlayOutgoingMessageAsync();
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"SendMessage: Outgoing audio notification failed: {ex.Message}");
                        }
                    });
                }
                catch { }

                MaybeCreateOutgoingMessageNotice(contact, message);

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

        /// <summary>
        /// Recompute the aggregated notification count (invites + notices), de-duping by origin
        /// and respecting any optimistic-cleared origins so the UI can update immediately.
        /// Safe to call from any thread; UI updates are marshalled to the UI thread.
        /// </summary>
        public void RefreshHasPendingInvites()
        {
            try
            {
                var invites = AppServices.ContactRequests.PendingInboundRequests.ToList();
                var notices = AppServices.Notifications.Notices.ToList();
                var securityEvents = AppServices.Notifications.SecurityEvents.ToList();

                var inviteOrigins = new HashSet<string>(invites.Where(p => !string.IsNullOrWhiteSpace(p.Uid)).Select(p => TrimUidPrefix(p.Uid!)), StringComparer.OrdinalIgnoreCase);
                var noticeOrigins = new HashSet<string>(notices.Where(n => !string.IsNullOrWhiteSpace(n.OriginUid)).Select(n => TrimUidPrefix(n.OriginUid!)), StringComparer.OrdinalIgnoreCase);

                // Exclude any optimistic-cleared origins so the UI reflects immediate user intent
                lock (_optimisticClearedLock)
                {
                    foreach (var o in _optimisticClearedOrigins) { inviteOrigins.Remove(o); noticeOrigins.Remove(o); }
                }

                // Build unique origin set (union of invites and notice origins)
                var uniqueOrigins = new HashSet<string>(inviteOrigins, StringComparer.OrdinalIgnoreCase);
                foreach (var o in noticeOrigins) uniqueOrigins.Add(o);

                // Count general notifications (alerts without origin - Info/Warning/Error)
                // Exclude invite notifications as they're already counted via inviteOrigins
                var generalAlerts = notices.Count(n => string.IsNullOrWhiteSpace(n.OriginUid) && !n.Title.Contains("Invite", StringComparison.OrdinalIgnoreCase));

                var total = uniqueOrigins.Count + generalAlerts + securityEvents.Count;

                // Marshal property updates to UI thread
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        NotificationCount = total;
                        HasPendingInvites = inviteOrigins.Count > 0;
                    }
                    catch { }
                }, Avalonia.Threading.DispatcherPriority.Send);

                // Prune optimistic-cleared origins that are now settled (no invites or notices remain)
                lock (_optimisticClearedLock)
                {
                    var toRemove = _optimisticClearedOrigins.Where(o => !inviteOrigins.Contains(o) && !noticeOrigins.Contains(o)).ToList();
                    foreach (var r in toRemove) _optimisticClearedOrigins.Remove(r);
                }
            }
            catch { }
        }

        public void AddOptimisticCleared(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin)) return;
            var o = TrimUidPrefix(origin);
            lock (_optimisticClearedLock) { _optimisticClearedOrigins.Add(o); }
            RefreshHasPendingInvites();
        }

        public void AddOptimisticClearedMany(IEnumerable<string>? origins)
        {
            if (origins == null) return;
            lock (_optimisticClearedLock)
            {
                foreach (var origin in origins)
                {
                    if (string.IsNullOrWhiteSpace(origin)) continue;
                    _optimisticClearedOrigins.Add(TrimUidPrefix(origin));
                }
            }
            RefreshHasPendingInvites();
        }

        public void RemoveOptimisticCleared(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin)) return;
            var o = TrimUidPrefix(origin);
            lock (_optimisticClearedLock) { _optimisticClearedOrigins.Remove(o); }
            RefreshHasPendingInvites();
        }

        public bool ShouldSuppressInvite(string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin)) return false;
            lock (_optimisticClearedLock)
            {
                if (_optimisticClearedOrigins.Contains(origin)) return true;
                var trimmed = TrimUidPrefix(origin);
                return _optimisticClearedOrigins.Contains(trimmed);
            }
        }

        private void HandleContactCameOnline(string uid)
        {
            try
            {
                var trimmed = TrimUidPrefix(uid);
                if (string.IsNullOrWhiteSpace(trimmed)) return;
                AppServices.Notifications.MarkConversationMessageNoticesRead(trimmed);
            }
            catch { }
        }

        private void PublishMessageNotification(string originUid, string displayName, Message message, bool incoming, bool unread)
        {
            var published = false;
            try
            {
                var trimmedOrigin = TrimUidPrefix(originUid);
                var title = incoming ? displayName : $"To {displayName}";
                var preview = BuildMessagePreview(message?.Content ?? string.Empty);
                var timestamp = message?.Timestamp ?? DateTime.UtcNow;
                AppServices.Notifications.AddOrUpdateMessageNotice(title, preview, trimmedOrigin, message?.Id ?? Guid.NewGuid(), incoming, timestamp, unread);
                published = true;
                
                // Audio notification is handled by NotificationService.AddOrUpdateMessageNotice
                // No need to duplicate audio calls here
            }
            catch (Exception ex)
            {
                try { Logger.Log($"PublishMessageNotification failed: incoming={incoming}, origin={originUid}, ex={ex.Message}"); } catch { }
                try { if (Zer0Talk.Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] PublishMessageNotification exception, will attempt fallback audio: {ex.Message}\n"); } catch { }
            }

            if (incoming && !published)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await AppServices.AudioNotifications.PlaySoundAsync(AudioNotificationService.SoundType.MessageIncoming, DateTime.UtcNow, "MainWindowViewModel.PublishMessageNotificationFallback");
                        try { if (Zer0Talk.Utilities.LoggingPaths.Enabled) Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.Audio, $"{DateTime.Now:O} [Audio] Fallback MessageIncoming playback triggered from MainWindowViewModel\n"); } catch { }
                    }
                    catch (Exception ex)
                    {
                        try { Logger.Log($"PublishMessageNotification fallback audio failed: {ex.Message}"); } catch { }
                    }
                });
            }
        }

        private void MaybeCreateOutgoingMessageNotice(Contact contact, Message message)
        {
            try
            {
                if (contact == null || message == null) return;
                var presence = contact.Presence;
                if (presence != PresenceStatus.Offline &&
                    presence != PresenceStatus.Invisible &&
                    presence != PresenceStatus.DoNotDisturb &&
                    presence != PresenceStatus.Idle)
                {
                    return;
                }
                // Don't create notifications for outgoing messages - users don't need to be notified about their own messages
                // var display = ResolveContactDisplayName(contact.UID);
                // PublishMessageNotification(contact.UID, display, message, incoming: false, unread: true);
            }
            catch { }
        }

        private bool IsSelfOnline()
        {
            try { return AppServices.Settings.Settings.Status == Models.PresenceStatus.Online; }
            catch { return true; }
        }

        private string ResolveContactDisplayName(string uid)
        {
            try
            {
                var trimmed = TrimUidPrefix(uid ?? string.Empty);
                if (string.IsNullOrWhiteSpace(trimmed)) return "Unknown";
                var contact = Contacts.FirstOrDefault(c => string.Equals(TrimUidPrefix(c.UID ?? string.Empty), trimmed, StringComparison.OrdinalIgnoreCase));
                if (contact != null && !string.IsNullOrWhiteSpace(contact.DisplayName)) return contact.DisplayName!;
                var storeContact = AppServices.Contacts.Contacts.FirstOrDefault(c => string.Equals(TrimUidPrefix(c.UID ?? string.Empty), trimmed, StringComparison.OrdinalIgnoreCase));
                if (storeContact != null && !string.IsNullOrWhiteSpace(storeContact.DisplayName)) return storeContact.DisplayName!;
                return trimmed;
            }
            catch { return uid; }
        }

        private static string BuildMessagePreview(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "<no text>";
            const int maxLen = 160;
            var trimmed = content.Trim();
            return trimmed.Length <= maxLen ? trimmed : string.Concat(trimmed.AsSpan(0, maxLen), "...");
        }

        public void FocusConversation(string uid)
        {
            try
            {
                var trimmed = TrimUidPrefix(uid ?? string.Empty);
                if (string.IsNullOrWhiteSpace(trimmed)) return;
                var contact = Contacts.FirstOrDefault(c => string.Equals(TrimUidPrefix(c.UID ?? string.Empty), trimmed, StringComparison.OrdinalIgnoreCase));
                if (contact == null) return;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => SelectedContact = contact);
            }
            catch { }
        }

        // Some parts of the code/store may use trimmed UIDs while others include the 'usr-' prefix.
        // Expand both forms so we can robustly remove notices regardless of representation.
        private IEnumerable<string> ExpandOriginForms(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin)) yield break;
            var trimmed = TrimUidPrefix(origin);
            yield return trimmed;
            var prefixed = trimmed.StartsWith("usr-", StringComparison.OrdinalIgnoreCase) ? trimmed : "usr-" + trimmed;
            if (!string.Equals(prefixed, trimmed, StringComparison.OrdinalIgnoreCase)) yield return prefixed;
        }

        // Mark a single notification-backed message as read and remove related notices for its origin.
        public void MarkMessageAsReadAndClear(Message m)
        {
            try
            {
                if (m == null) return;
                // Clear notification badge for this message
                try { AppServices.Notifications.MarkMessageNoticeRead(m.Id); } catch { }
                RefreshHasPendingInvites();
            }
            catch { }
        }

        // Mark all messages shown in the Messages list as read and remove related notices.
        public void MarkAllNotificationMessagesReadAndClear()
        {
            try
            {
                AppServices.Notifications.MarkAllMessageNoticesRead();
                RefreshHasPendingInvites();
            }
            catch { }
        }

        // Mark all unread incoming messages from all contacts as read (used when coming back online)
        public void MarkAllUnreadMessagesAsRead()
        {
            try
            {
                // Clear all conversation notifications
                try { AppServices.Notifications.MarkAllMessageNoticesRead(); } catch { }
                RefreshHasPendingInvites();
            }
            catch { }
        }

        public void HandleLocalPresenceStatusChanged(Models.PresenceStatus status)
        {
            try
            {
                if (status != Models.PresenceStatus.Online) return;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try { MarkAllUnreadMessagesAsRead(); }
                    catch { }
                });
            }
            catch { }
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
                    try { Zer0Talk.Utilities.ErrorLogger.LogException(ex, source: "UI.LinkPreviewFetch"); } catch { }
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
            _ = Task.Run(async () =>
            {
                try
                {
                    var normalized = TrimUidPrefix(recipientUid);
                    if (string.IsNullOrWhiteSpace(normalized)) return;
                    for (var i = 0; i < 4; i++)
                    {
                        if (AppServices.Network.HasEncryptedSession(normalized))
                        {
                            await AppServices.Outbox.DrainAsync(normalized, AppServices.Passphrase, CancellationToken.None);
                            return;
                        }
                        await Task.Delay(300).ConfigureAwait(false);
                    }
                }
                catch { }
            });
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowOfflineBanner(banner));
            // Don't create notifications for outgoing messages - users don't need to be notified about their own messages
            // var display = ResolveContactDisplayName(recipientUid);
            // PublishMessageNotification(recipientUid, display, message, incoming: false, unread: true);
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
                    if (message.Id == Guid.Empty) continue;
                    var sender = TrimUidPrefix(message.SenderUID ?? string.Empty);
                    if (!string.Equals(sender, peerUid, StringComparison.OrdinalIgnoreCase)) continue;

                    try { AppServices.Notifications.MarkMessageNoticeRead(message.Id); } catch { }
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

                try { Logger.NetworkLog($"SimSend: peer={recipientUid} id={outbound.Id}"); } catch { }

                // Create simulated echo response from contact
                var echo = new Message
                {
                    Id = Guid.NewGuid(),
                    SenderUID = recipientUid,
                    RecipientUID = outbound.SenderUID,
                    Content = content,
                    Timestamp = DateTime.UtcNow,
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
                var start = msg.Timestamp;
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

                // Drain all queued messages from outbox
                _ = Task.Run(async () =>
                {
                    try { await AppServices.Outbox.DrainAsync(targetUid, AppServices.Passphrase, CancellationToken.None); } catch { }
                });
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
        /// <summary>
        /// Shows a proactive banner when the selected contact is not fully online.
        /// Invisible contacts show the same "offline" message (privacy: don't reveal invisible status).
        /// </summary>
        private void UpdatePresenceBanner(Contact? contact)
        {
            if (contact == null || contact.IsSimulated) { HideOfflineBanner(); return; }
            switch (contact.Presence)
            {
                case PresenceStatus.Offline:
                case PresenceStatus.Invisible: // Don't reveal invisible — treat as offline
                    ShowOfflineBanner("Contact is offline. Messages will be sent when they come online.");
                    break;
                case PresenceStatus.Idle:
                    ShowOfflineBanner("Contact is away. Messages will be delivered when they return.");
                    break;
                case PresenceStatus.DoNotDisturb:
                    ShowOfflineBanner("Contact is in Do Not Disturb. Messages will be delivered.");
                    break;
                case PresenceStatus.Online:
                default:
                    HideOfflineBanner();
                    break;
            }
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

        // Localized strings for MainWindow UI
        public string LocalizedContacts => Services.AppServices.Localization.GetString("MainWindow.Contacts", "Contacts");
        public string LocalizedAddContact => Services.AppServices.Localization.GetString("MainWindow.AddContact", "Add Contact");
        public string LocalizedViewProfile => Services.AppServices.Localization.GetString("MainWindow.ViewProfile", "View Profile");
        public string LocalizedCopyUID => Services.AppServices.Localization.GetString("MainWindow.CopyUID", "Copy UID");
        public string LocalizedSetSimulatedPresence => Services.AppServices.Localization.GetString("MainWindow.SetSimulatedPresence", "Set simulated presence");
        public string LocalizedRemoveContact => Services.AppServices.Localization.GetString("MainWindow.RemoveContact", "Remove contact");
        public string LocalizedSimulated => Services.AppServices.Localization.GetString("MainWindow.Simulated", "Sim");
        public string LocalizedTrusted => Services.AppServices.Localization.GetString("MainWindow.Trusted", "Trusted");
        public string LocalizedEncrypted => Services.AppServices.Localization.GetString("MainWindow.Encrypted", "Encrypted");
        public string LocalizedBurnConversation => Services.AppServices.Localization.GetString("MainWindow.BurnConversation", "Burn conversation permanently");
        public string LocalizedChatEncrypted => Services.AppServices.Localization.GetString("MainWindow.ChatEncrypted", "Chat is end-to-end encrypted");
        public string LocalizedSimulatedContact => Services.AppServices.Localization.GetString("MainWindow.SimulatedContact", "Simulated contact (loopback)");
        public string LocalizedBold => Services.AppServices.Localization.GetString("MainWindow.Bold", "Bold (**text**)");
        public string LocalizedItalic => Services.AppServices.Localization.GetString("MainWindow.Italic", "Italic (*text*)");
        public string LocalizedUnderline => Services.AppServices.Localization.GetString("MainWindow.Underline", "Underline (__text__)");
        public string LocalizedStrikeThrough => Services.AppServices.Localization.GetString("MainWindow.StrikeThrough", "Strike-through (~~text~~)");
        public string LocalizedQuote => Services.AppServices.Localization.GetString("MainWindow.Quote", "Quote (> text)");
        public string LocalizedCode => Services.AppServices.Localization.GetString("MainWindow.Code", "Code (`text` or ```block```)");
        public string LocalizedSpoiler => Services.AppServices.Localization.GetString("MainWindow.Spoiler", "Spoiler (||text||)");
        public string LocalizedTypeMessage => Services.AppServices.Localization.GetString("MainWindow.TypeMessage", "Type a message");
        public string LocalizedSendMessage => Services.AppServices.Localization.GetString("MainWindow.SendMessage", "Send message");
        public string LocalizedJumpToPresent => Services.AppServices.Localization.GetString("MainWindow.JumpToPresent", "Jump to present");
        public string LocalizedPendingInvites => Services.AppServices.Localization.GetString("MainWindow.PendingInvites", "Pending Invites");
        public string LocalizedPrevious => Services.AppServices.Localization.GetString("MainWindow.Previous", "Previous");
        public string LocalizedNext => Services.AppServices.Localization.GetString("MainWindow.Next", "Next");
        public string LocalizedClearInvites => Services.AppServices.Localization.GetString("MainWindow.ClearInvites", "Clear Invites");
        public string LocalizedMarkAllRead => Services.AppServices.Localization.GetString("MainWindow.MarkAllRead", "Mark all messages read");
        public string LocalizedClearAllAlerts => Services.AppServices.Localization.GetString("MainWindow.ClearAllAlerts", "Clear All Alerts");
        public string LocalizedMessages => Services.AppServices.Localization.GetString("MainWindow.Messages", "Messages");
        public string LocalizedAlerts => Services.AppServices.Localization.GetString("MainWindow.Alerts", "Alerts");
        public string LocalizedThemeEditor => Services.AppServices.Localization.GetString("MainWindow.ThemeEditor", "Theme Editor");
        public string LocalizedAccept => Services.AppServices.Localization.GetString("MainWindow.Accept", "Accept");
        public string LocalizedReject => Services.AppServices.Localization.GetString("MainWindow.Reject", "Reject");
        public string LocalizedNoPendingInvites => Services.AppServices.Localization.GetString("MainWindow.NoPendingInvites", "No pending invites.");
        public string LocalizedNoRecentMessages => Services.AppServices.Localization.GetString("MainWindow.NoRecentMessages", "No recent messages.");
        public string LocalizedNoAlerts => Services.AppServices.Localization.GetString("MainWindow.NoAlerts", "No alerts.");
        public string LocalizedPrevName => Services.AppServices.Localization.GetString("MainWindow.PrevName", "Prev. Name");
        public string LocalizedChanges => Services.AppServices.Localization.GetString("MainWindow.Changes", "Changes");
        public string LocalizedIdentityLabel => Services.AppServices.Localization.GetString("MainWindow.Identity", "Identity");
        public string LocalizedDisplayNameLabel => Services.AppServices.Localization.GetString("Settings.DisplayName", "Display Name");
        public string LocalizedUidLabel => Services.AppServices.Localization.GetString("Settings.UID", "UID");
        public string LocalizedPresenceLabel => Services.AppServices.Localization.GetString("MainWindow.Presence", "Presence");
        public string LocalizedPublicKeyLabel => Services.AppServices.Localization.GetString("Settings.PublicKey", "Public Key");
        public string LocalizedBioLabel => Services.AppServices.Localization.GetString("Settings.Bio", "Bio");
        public string LocalizedCopyPublicKey => Services.AppServices.Localization.GetString("MainWindow.CopyPublicKey", "Copy Public Key");
        public string LocalizedSave => Services.AppServices.Localization.GetString("Common.Save", "Save");
        public string LocalizedCloseEsc => Services.AppServices.Localization.GetString("MainWindow.CloseEsc", "Close (Esc)");
        public string LocalizedVerify => Services.AppServices.Localization.GetString("MainWindow.Verify", "Verify");
        public string LocalizedSaveSimulated => Services.AppServices.Localization.GetString("MainWindow.SaveSimulated", "Save (Simulated)");
        public string LocalizedMessagePending => Services.AppServices.Localization.GetString("MainWindow.MessagePending", "Message pending - contact is offline");
        public string LocalizedSending => Services.AppServices.Localization.GetString("MainWindow.Sending", "Sending message...");
        public string LocalizedMessageSent => Services.AppServices.Localization.GetString("MainWindow.MessageSent", "Message sent");
        public string LocalizedMessageRead => Services.AppServices.Localization.GetString("MainWindow.MessageRead", "Message read by contact");
        public string LocalizedMessageReceived => Services.AppServices.Localization.GetString("MainWindow.MessageReceived", "Message received");
        public string LocalizedEdited => Services.AppServices.Localization.GetString("MainWindow.Edited", "(edited)");
        public string LocalizedEdit => Services.AppServices.Localization.GetString("MainWindow.Edit", "Edit");
        public string LocalizedDelete => Services.AppServices.Localization.GetString("MainWindow.Delete", "Delete");
        public string LocalizedToggleContacts => Services.AppServices.Localization.GetString("MainWindow.ToggleContacts", "Toggle Contacts");
        public string LocalizedOpenNotifications => Services.AppServices.Localization.GetString("MainWindow.OpenNotifications", "Open Notifications");
        public string LocalizedLockLogout => Services.AppServices.Localization.GetString("MainWindow.LockLogout", "Lock / Logout");
        public string LocalizedMinimize => Services.AppServices.Localization.GetString("MainWindow.Minimize", "Minimize");
        public string LocalizedMaximize => Services.AppServices.Localization.GetString("MainWindow.Maximize", "Maximize / Restore");
        public string LocalizedClose => Services.AppServices.Localization.GetString("MainWindow.Close", "Close");
        public string LocalizedHome => Services.AppServices.Localization.GetString("MainWindow.Home", "Home");
        public string LocalizedMonitoring => Services.AppServices.Localization.GetString("MainWindow.Monitoring", "Monitoring");
        public string LocalizedLogs => Services.AppServices.Localization.GetString("MainWindow.Logs", "Logs");
        public string LocalizedSettings => Services.AppServices.Localization.GetString("MainWindow.Settings", "Settings");
        public string LocalizedAbout => Services.AppServices.Localization.GetString("MainWindow.About", "About");
        public string LocalizedOnline => Services.AppServices.Localization.GetString("Settings.Online", "Online");
        public string LocalizedAway => Services.AppServices.Localization.GetString("Settings.Away", "Away");
        public string LocalizedDoNotDisturb => Services.AppServices.Localization.GetString("Settings.DoNotDisturb", "Do Not Disturb");
        public string LocalizedInvisible => Services.AppServices.Localization.GetString("MainWindow.Invisible", "Invisible");
        public string LocalizedOffline => Services.AppServices.Localization.GetString("Settings.Offline", "Offline");

        private void RefreshLocalizedStrings()
        {
            OnPropertyChanged(nameof(LocalizedContacts));
            OnPropertyChanged(nameof(LocalizedAddContact));
            OnPropertyChanged(nameof(LocalizedViewProfile));
            OnPropertyChanged(nameof(LocalizedCopyUID));
            OnPropertyChanged(nameof(LocalizedSetSimulatedPresence));
            OnPropertyChanged(nameof(LocalizedRemoveContact));
            OnPropertyChanged(nameof(LocalizedSimulated));
            OnPropertyChanged(nameof(LocalizedTrusted));
            OnPropertyChanged(nameof(LocalizedEncrypted));
            OnPropertyChanged(nameof(LocalizedBurnConversation));
            OnPropertyChanged(nameof(LocalizedChatEncrypted));
            OnPropertyChanged(nameof(LocalizedSimulatedContact));
            OnPropertyChanged(nameof(LocalizedBold));
            OnPropertyChanged(nameof(LocalizedItalic));
            OnPropertyChanged(nameof(LocalizedUnderline));
            OnPropertyChanged(nameof(LocalizedStrikeThrough));
            OnPropertyChanged(nameof(LocalizedQuote));
            OnPropertyChanged(nameof(LocalizedCode));
            OnPropertyChanged(nameof(LocalizedSpoiler));
            OnPropertyChanged(nameof(LocalizedTypeMessage));
            OnPropertyChanged(nameof(LocalizedSendMessage));
            OnPropertyChanged(nameof(LocalizedJumpToPresent));
            OnPropertyChanged(nameof(LocalizedPendingInvites));
            OnPropertyChanged(nameof(LocalizedPrevious));
            OnPropertyChanged(nameof(LocalizedNext));
            OnPropertyChanged(nameof(LocalizedClearInvites));
            OnPropertyChanged(nameof(LocalizedMarkAllRead));
            OnPropertyChanged(nameof(LocalizedClearAllAlerts));
            OnPropertyChanged(nameof(LocalizedMessages));
            OnPropertyChanged(nameof(LocalizedAlerts));
            OnPropertyChanged(nameof(LocalizedThemeEditor));
            OnPropertyChanged(nameof(LocalizedAccept));
            OnPropertyChanged(nameof(LocalizedReject));
            OnPropertyChanged(nameof(LocalizedNoPendingInvites));
            OnPropertyChanged(nameof(LocalizedNoRecentMessages));
            OnPropertyChanged(nameof(LocalizedNoAlerts));
            OnPropertyChanged(nameof(LocalizedPrevName));
            OnPropertyChanged(nameof(LocalizedChanges));
            OnPropertyChanged(nameof(LocalizedIdentityLabel));
            OnPropertyChanged(nameof(LocalizedDisplayNameLabel));
            OnPropertyChanged(nameof(LocalizedUidLabel));
            OnPropertyChanged(nameof(LocalizedPresenceLabel));
            OnPropertyChanged(nameof(LocalizedPublicKeyLabel));
            OnPropertyChanged(nameof(LocalizedBioLabel));
            OnPropertyChanged(nameof(LocalizedCopyPublicKey));
            OnPropertyChanged(nameof(LocalizedSave));
            OnPropertyChanged(nameof(LocalizedCloseEsc));
            OnPropertyChanged(nameof(LocalizedVerify));
            OnPropertyChanged(nameof(LocalizedSaveSimulated));
            OnPropertyChanged(nameof(LocalizedMessagePending));
            OnPropertyChanged(nameof(LocalizedSending));
            OnPropertyChanged(nameof(LocalizedMessageSent));
            OnPropertyChanged(nameof(LocalizedMessageRead));
            OnPropertyChanged(nameof(LocalizedMessageReceived));
            OnPropertyChanged(nameof(LocalizedEdited));
            OnPropertyChanged(nameof(LocalizedEdit));
            OnPropertyChanged(nameof(LocalizedDelete));
            OnPropertyChanged(nameof(LocalizedToggleContacts));
            OnPropertyChanged(nameof(LocalizedOpenNotifications));
            OnPropertyChanged(nameof(LocalizedLockLogout));
            OnPropertyChanged(nameof(LocalizedMinimize));
            OnPropertyChanged(nameof(LocalizedMaximize));
            OnPropertyChanged(nameof(LocalizedClose));
            OnPropertyChanged(nameof(LocalizedHome));
            OnPropertyChanged(nameof(LocalizedMonitoring));
            OnPropertyChanged(nameof(LocalizedLogs));
            OnPropertyChanged(nameof(LocalizedSettings));
            OnPropertyChanged(nameof(LocalizedAbout));
            OnPropertyChanged(nameof(LocalizedOnline));
            OnPropertyChanged(nameof(LocalizedAway));
            OnPropertyChanged(nameof(LocalizedDoNotDisturb));
            OnPropertyChanged(nameof(LocalizedInvisible));
            OnPropertyChanged(nameof(LocalizedOffline));
            OnPropertyChanged(nameof(LocalizedConnectedPeers));
            OnPropertyChanged(nameof(LocalizedStreamerMode));
            OnPropertyChanged(nameof(LocalizedStreamerModeActive));
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
                var start = m.Timestamp;
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
                Logger.Log($"LoadConversation: Loading conversation for peer={peerUid}");
                Messages.Clear();
                var list = _messagesStore.LoadMessages(peerUid, AppServices.Passphrase);
                Logger.Log($"LoadConversation: Loaded {list.Count} messages for peer={peerUid}");
                foreach (var m in list) Messages.Add(m);
                MarkMessagesAsRead();
                // Clear notifications for this conversation
                try { AppServices.Notifications.MarkConversationMessageNoticesRead(peerUid); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Log($"LoadConversation: Error loading conversation for peer={peerUid}: {ex.Message}");
            }
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
            
            // Get localized strings
            var title = Services.AppServices.Localization.GetString("Dialogs.BurnConversationTitle", "Burn conversation");
            var messageTemplate = Services.AppServices.Localization.GetString("Dialogs.BurnConversationMessage", 
                "Burn conversation with {0}?\n\n" +
                "This process performs irreversible destruction:\n" +
                "• Overwrites stored messages with pseudorandom 0/1 patterns\n" +
                "• Rewrites the archive with randomized lorem gibberish\n" +
                "• Applies an alternating 1/0 sweep followed by an all-zero pass\n" +
                "• Deletes residual message and outbox files\n\n" +
                "Once completed, the conversation cannot be recovered.");
            var confirmationText = string.Format(messageTemplate, display);
            var burnNowText = Services.AppServices.Localization.GetString("Dialogs.BurnNow", "Burn Now");
            var cancelText = Services.AppServices.Localization.GetString("Common.Cancel", "Cancel");

            // Play warning sound when dialog appears
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await AppServices.AudioNotifications.PlayCustomSoundAsync("ui-10-smooth-warnnotify-sound-effect-365842.mp3");
                }
                catch { }
            });

            bool confirmed;
            try
            {
                confirmed = await AppServices.Dialogs.ConfirmDestructiveAsync(title, confirmationText, burnNowText, cancelText);
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

                var useEnhanced = AppServices.Settings.Settings?.UseEnhancedMessageBurn ?? false;
                var summary = AppServices.Retention.BurnConversationSecurely(peerUid, AppServices.Passphrase, useEnhanced);
                
                try
                {
                    LoadConversation(peerUid);
                }
                catch (Exception loadEx)
                {
                    // Log load conversation error but don't fail the burn operation
                    try { Zer0Talk.Utilities.ErrorLogger.LogException(loadEx, source: "UI.BurnConversation.LoadConversation"); } catch { }
                }

                var toast = summary.BytesWiped > 0
                    ? $"Secure shred complete ({summary.BytesWiped:N0} bytes wiped)."
                    : "Conversation artifacts were already absent.";
                
                try
                {
                    await AppServices.Dialogs.ShowSuccessAsync("Conversation burned", toast, 3500);
                }
                catch (Exception toastEx)
                {
                    // Log toast error but don't fail the burn operation
                    try { Zer0Talk.Utilities.ErrorLogger.LogException(toastEx, source: "UI.BurnConversation.Toast"); } catch { }
                }
                
                // Clear error message since burn was successful
                ErrorMessage = string.Empty;
            }
            catch (Exception ex)
            {
                ErrorMessage = "Burn failed. Check logs for details.";
                try { Zer0Talk.Utilities.ErrorLogger.LogException(ex, source: "UI.BurnConversation"); } catch { }
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
