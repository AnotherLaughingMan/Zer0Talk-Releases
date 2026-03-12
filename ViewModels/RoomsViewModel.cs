using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Threading;

using Zer0Talk.Services;
using Zer0Talk.Utilities;

namespace Zer0Talk.ViewModels;

/// <summary>ViewModel row for a joined room.</summary>
public sealed class RoomEntry : INotifyPropertyChanged
{
    private bool _adminOnline;

    public string RoomId     { get; }
    public bool AdminOnline
    {
        get => _adminOnline;
        set { _adminOnline = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusIcon)); }
    }
    public string StatusIcon => AdminOnline ? "●" : "○";
    public string StatusText => AdminOnline ? "Admin online" : "Server-routed";

    public RoomEntry(string roomId) => RoomId = roomId;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>ViewModel for the Rooms window: server connection, room list, member management and invites.</summary>
public sealed class RoomsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly RoomService          _rooms   = AppServices.Rooms;
    private readonly ServerAccountService _account = AppServices.ServerAccount;

    // ═══════════════════════════════════════════════════════════
    //  Server connection
    // ═══════════════════════════════════════════════════════════

    private string _homeServerText = AppServices.Settings.Settings.HomeServer ?? string.Empty;
    private string _connectStatus  = "Not connected";
    private bool   _isConnected;
    private bool   _isConnecting;

    public string HomeServerText
    {
        get => _homeServerText;
        set { _homeServerText = value; OnPropertyChanged(); (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    }

    public string ConnectStatus
    {
        get => _connectStatus;
        private set { _connectStatus = value; OnPropertyChanged(); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            _isConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDisconnected));
            (ConnectCommand    as RelayCommand)?.RaiseCanExecuteChanged();
            (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CreateRoomCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (InviteCommand     as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool IsDisconnected => !_isConnected;

    // ═══════════════════════════════════════════════════════════
    //  Room list
    // ═══════════════════════════════════════════════════════════

    public ObservableCollection<RoomEntry> Rooms { get; } = new();

    private RoomEntry? _selectedRoom;
    public RoomEntry? SelectedRoom
    {
        get => _selectedRoom;
        set
        {
            _selectedRoom = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedRoomId));
            OnPropertyChanged(nameof(SelectedRoomStatusText));
            OnPropertyChanged(nameof(HasSelectedRoom));
            OnPropertyChanged(nameof(HasNoSelectedRoom));
            _ = RefreshMembersAsync();
            (LeaveRoomCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (InviteCommand    as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string? SelectedRoomId       => _selectedRoom?.RoomId;
    public string  SelectedRoomStatusText => _selectedRoom?.StatusText ?? string.Empty;
    public bool    HasSelectedRoom      => _selectedRoom != null;
    public bool    HasNoSelectedRoom    => _selectedRoom == null;

    // ═══════════════════════════════════════════════════════════
    //  Member list
    // ═══════════════════════════════════════════════════════════

    public ObservableCollection<RoomMember> Members { get; } = new();

    private RoomMember? _selectedMember;
    public RoomMember? SelectedMember
    {
        get => _selectedMember;
        set
        {
            _selectedMember = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedMember));
            OnPropertyChanged(nameof(SelectedMemberLabel));
            (KickMemberCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BanMemberCommand  as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool    HasSelectedMember  => _selectedMember != null;
    public string  SelectedMemberLabel => _selectedMember != null ? $"Selected: {_selectedMember.Uid}" : string.Empty;

    private string _memberCountText = string.Empty;
    public string MemberCountText
    {
        get => _memberCountText;
        private set { _memberCountText = value; OnPropertyChanged(); }
    }

    // ═══════════════════════════════════════════════════════════
    //  Create room form
    // ═══════════════════════════════════════════════════════════

    private string _newRoomName = string.Empty;
    private string _newRoomCap  = "20";

    public string NewRoomName
    {
        get => _newRoomName;
        set { _newRoomName = value; OnPropertyChanged(); (CreateRoomCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    }

    public string NewRoomCap
    {
        get => _newRoomCap;
        set { _newRoomCap = value; OnPropertyChanged(); }
    }

    // ═══════════════════════════════════════════════════════════
    //  Invite form
    // ═══════════════════════════════════════════════════════════

    private string _inviteUid        = string.Empty;
    private string _inviteHomeServer = string.Empty;

    public string InviteUid
    {
        get => _inviteUid;
        set { _inviteUid = value; OnPropertyChanged(); (InviteCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    }

    public string InviteHomeServer
    {
        get => _inviteHomeServer;
        set { _inviteHomeServer = value; OnPropertyChanged(); }
    }

    // ═══════════════════════════════════════════════════════════
    //  Commands
    // ═══════════════════════════════════════════════════════════

    public ICommand ConnectCommand        { get; }
    public ICommand DisconnectCommand     { get; }
    public ICommand CreateRoomCommand     { get; }
    public ICommand InviteCommand         { get; }
    public ICommand LeaveRoomCommand      { get; }
    public ICommand RefreshMembersCommand { get; }
    public ICommand KickMemberCommand     { get; }
    public ICommand BanMemberCommand      { get; }

    public RoomsViewModel()
    {
        ConnectCommand    = new RelayCommand(_ => _ = ConnectAsync(),
                                            _ => !_isConnected && !_isConnecting && !string.IsNullOrWhiteSpace(_homeServerText));
        DisconnectCommand = new RelayCommand(_ => Disconnect(),    _ => _isConnected);
        CreateRoomCommand = new RelayCommand(_ => _ = CreateRoomAsync(),
                                            _ => _isConnected && !string.IsNullOrWhiteSpace(_newRoomName));
        InviteCommand     = new RelayCommand(_ => _ = InviteAsync(),
                                            _ => _isConnected && _selectedRoom != null && !string.IsNullOrWhiteSpace(_inviteUid));
        LeaveRoomCommand  = new RelayCommand(_ => _ = LeaveRoomAsync(), _ => _selectedRoom != null);
        RefreshMembersCommand = new RelayCommand(_ => _ = RefreshMembersAsync());
        KickMemberCommand = new RelayCommand(_ => _ = KickMemberAsync(),
                                            _ => _isConnected && _selectedMember != null && _selectedRoom != null);
        BanMemberCommand  = new RelayCommand(_ => _ = BanMemberAsync(),
                                            _ => _isConnected && _selectedMember != null && _selectedRoom != null);

        _rooms.MemberJoined     += OnMemberJoined;
        _rooms.MemberLeft       += OnMemberLeft;
        _rooms.AdminOnline      += OnAdminOnline;
        _rooms.AdminOffline     += OnAdminOffline;
        _account.ConnectionLost += OnConnectionLost;

        LoadSavedRooms();
        IsConnected   = _account.IsConnected && _account.IsAuthenticated;
        ConnectStatus = IsConnected
            ? $"Connected to {AppServices.Settings.Settings.HomeServer}"
            : "Not connected";
    }

    // ─── Room list init ────────────────────────────────────────

    private void LoadSavedRooms()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Rooms) existing.Add(r.RoomId);

        foreach (var id in AppServices.Settings.Settings.JoinedRoomIds)
        {
            if (!string.IsNullOrWhiteSpace(id) && !existing.Contains(id))
                Rooms.Add(new RoomEntry(id) { AdminOnline = _rooms.IsAdminOnline(id) });
        }
    }

    // ─── Connection ────────────────────────────────────────────

    private async Task ConnectAsync()
    {
        if (_isConnecting || _isConnected) return;
        _isConnecting = true;
        ConnectStatus = "Connecting…";
        (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        try
        {
            AppServices.Settings.Settings.HomeServer = HomeServerText.Trim();
            _ = Task.Run(() => { try { AppServices.Settings.Save(AppServices.Passphrase); } catch { } });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var ok = await _account.ConnectAsync(cts.Token);
            IsConnected   = ok;
            ConnectStatus = ok
                ? $"Connected to {HomeServerText}"
                : "Connection failed — check address and retry";
            if (ok) LoadSavedRooms();
        }
        catch (OperationCanceledException)
        {
            ConnectStatus = "Connection timed out";
            IsConnected   = false;
        }
        catch (Exception ex)
        {
            ConnectStatus = $"Error: {ex.Message}";
            IsConnected   = false;
        }
        finally
        {
            _isConnecting = false;
            (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private void Disconnect()
    {
        _account.Disconnect();
        IsConnected   = false;
        ConnectStatus = "Disconnected";
    }

    // ─── Room management ───────────────────────────────────────

    private async Task CreateRoomAsync()
    {
        if (!int.TryParse(_newRoomCap, out var cap) || cap < 2) cap = 20;
        var roomId = await _rooms.CreateRoomAsync(_newRoomName.Trim(), cap);
        if (roomId != null)
        {
            var entry = new RoomEntry(roomId);
            Dispatcher.UIThread.Post(() => { Rooms.Add(entry); SelectedRoom = entry; });
            NewRoomName = string.Empty;
        }
        else
        {
            await AppServices.Dialogs.ShowErrorAsync("Create Room Failed", "The server rejected the room creation request.");
        }
    }

    private async Task InviteAsync()
    {
        if (_selectedRoom == null || string.IsNullOrWhiteSpace(_inviteUid)) return;
        var hs = string.IsNullOrWhiteSpace(_inviteHomeServer) ? null : _inviteHomeServer.Trim();
        var ok = await _rooms.InviteAsync(_selectedRoom.RoomId, _inviteUid.Trim(), hs);
        if (ok)
        {
            await AppServices.Dialogs.ShowSuccessAsync("Invite Sent", $"Invite sent to {_inviteUid}.");
            InviteUid = string.Empty;
        }
        else
        {
            await AppServices.Dialogs.ShowErrorAsync("Invite Failed", "Could not send invite. Check UID and your room role.");
        }
    }

    private async Task LeaveRoomAsync()
    {
        if (_selectedRoom == null) return;
        var confirm = await AppServices.Dialogs.ConfirmDestructiveAsync(
            "Leave Room", $"Leave room {_selectedRoom.RoomId}?\nYou will need a new invite to rejoin.", "Leave");
        if (!confirm) return;
        var ok = await _rooms.LeaveAsync(_selectedRoom.RoomId);
        if (ok)
        {
            var removed = _selectedRoom;
            Dispatcher.UIThread.Post(() => { Rooms.Remove(removed); SelectedRoom = null; Members.Clear(); MemberCountText = string.Empty; });
        }
        else
        {
            await AppServices.Dialogs.ShowErrorAsync("Leave Failed", "Could not leave the room.");
        }
    }

    internal async Task RefreshMembersAsync()
    {
        if (_selectedRoom == null) { Members.Clear(); MemberCountText = string.Empty; return; }
        var list = await _rooms.GetMembersAsync(_selectedRoom.RoomId);
        Dispatcher.UIThread.Post(() =>
        {
            Members.Clear();
            foreach (var m in list) Members.Add(m);
            MemberCountText = $"{list.Count} member{(list.Count == 1 ? "" : "s")}";
        });
    }

    private async Task KickMemberAsync()
    {
        if (_selectedRoom == null || _selectedMember == null) return;
        var confirm = await AppServices.Dialogs.ConfirmDestructiveAsync(
            "Kick Member", $"Kick {_selectedMember.Uid} from the room?", "Kick");
        if (!confirm) return;
        var ok = await _rooms.KickAsync(_selectedRoom.RoomId, _selectedMember.Uid);
        if (ok) await RefreshMembersAsync();
        else await AppServices.Dialogs.ShowErrorAsync("Kick Failed", "Could not kick this member. Check your role.");
    }

    private async Task BanMemberAsync()
    {
        if (_selectedRoom == null || _selectedMember == null) return;
        var confirm = await AppServices.Dialogs.ConfirmDestructiveAsync(
            "Ban Member", $"Ban {_selectedMember.Uid}?\nThey will be removed and cannot rejoin.", "Ban");
        if (!confirm) return;
        var ok = await _rooms.BanAsync(_selectedRoom.RoomId, _selectedMember.Uid);
        if (ok) await RefreshMembersAsync();
        else await AppServices.Dialogs.ShowErrorAsync("Ban Failed", "Could not ban this member. Check your role.");
    }

    // ─── RoomService push event handlers ───────────────────────

    private void OnMemberJoined(string roomId, string uid) =>
        Dispatcher.UIThread.Post(() => { if (_selectedRoom?.RoomId == roomId) _ = RefreshMembersAsync(); });

    private void OnMemberLeft(string roomId, string uid) =>
        Dispatcher.UIThread.Post(() => { if (_selectedRoom?.RoomId == roomId) _ = RefreshMembersAsync(); });

    private void OnAdminOnline(string roomId, string relayKey) =>
        Dispatcher.UIThread.Post(() => { foreach (var r in Rooms) if (r.RoomId == roomId) r.AdminOnline = true; });

    private void OnAdminOffline(string roomId) =>
        Dispatcher.UIThread.Post(() => { foreach (var r in Rooms) if (r.RoomId == roomId) r.AdminOnline = false; });

    private void OnConnectionLost() =>
        Dispatcher.UIThread.Post(() => { IsConnected = false; ConnectStatus = "Connection lost"; });

    // ─── INPC / IDisposable ────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public void Dispose()
    {
        _rooms.MemberJoined     -= OnMemberJoined;
        _rooms.MemberLeft       -= OnMemberLeft;
        _rooms.AdminOnline      -= OnAdminOnline;
        _rooms.AdminOffline     -= OnAdminOffline;
        _account.ConnectionLost -= OnConnectionLost;
    }
}
