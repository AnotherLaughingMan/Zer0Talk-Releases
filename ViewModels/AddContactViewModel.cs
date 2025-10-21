using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

using Zer0Talk.Services;
using Models = Zer0Talk.Models;

namespace Zer0Talk.ViewModels
{
    public class AddContactViewModel : INotifyPropertyChanged
    {
        private string _uid = string.Empty;
        private string _host = string.Empty;
    private int _port;
        private bool _busy;
        private string _status = string.Empty;
        // [VERIFY] Optional expected public key (hex) to validate identity after connect
        private string _expectedPublicKeyHex = string.Empty;
        // [TEST] If true, add contact locally without peer verification/lookup.
        private bool _isSimulated;
    // Exposed for XAML to hide simulated contact checkbox in Release builds.
#if DEBUG
    public bool ShowSimulatedOption => true;
#else
    public bool ShowSimulatedOption => false;
#endif

        public string UID { get => _uid; set { _uid = value; OnPropertyChanged(); (SendCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }
        public string Host { get => _host; set { _host = value; OnPropertyChanged(); } }
        public int Port { get => _port; set { _port = value; OnPropertyChanged(); } }
        public bool IsBusy { get => _busy; set { _busy = value; OnPropertyChanged(); } }
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public string ExpectedPublicKeyHex { get => _expectedPublicKeyHex; set { _expectedPublicKeyHex = value; OnPropertyChanged(); } }
        public bool IsSimulated { get => _isSimulated; set { _isSimulated = value; OnPropertyChanged(); (SendCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }

        public ICommand SendCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action<bool>? CloseRequested;

        // Localized strings
        public string LocalizedTitle => Services.AppServices.Localization.GetString("AddContact.Title", "Add Contact");
        public string LocalizedEnterUID => Services.AppServices.Localization.GetString("AddContact.EnterUID", "Enter the contact's UID (no prefix). Discovery is automatic.");
        public string LocalizedUID => Services.AppServices.Localization.GetString("AddContact.UID", "UID");
        public string LocalizedUIDPlaceholder => Services.AppServices.Localization.GetString("AddContact.UIDPlaceholder", "e.g., 7XKQ9Z8P");
        public string LocalizedExpectedPublicKey => Services.AppServices.Localization.GetString("AddContact.ExpectedPublicKey", "Expected Public Key (hex, optional)");
        public string LocalizedPublicKeyPlaceholder => Services.AppServices.Localization.GetString("AddContact.PublicKeyPlaceholder", "abcdef...");
        public string LocalizedSimulatedContact => Services.AppServices.Localization.GetString("AddContact.SimulatedContact", "Simulated Contact");
        public string LocalizedCancel => Services.AppServices.Localization.GetString("AddContact.Cancel", "Cancel");
        public string LocalizedSendRequest => Services.AppServices.Localization.GetString("AddContact.SendRequest", "Send Request");

        public AddContactViewModel()
        {
            SendCommand = new RelayCommand(async _ => await SendAsync(), _ => !string.IsNullOrWhiteSpace(UID));
            CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(false));

            // Subscribe to language changes
            try
            {
                Action languageChangedHandler = () => { Avalonia.Threading.Dispatcher.UIThread.Post(RefreshLocalizedStrings); };
                AppServices.Localization.LanguageChanged += languageChangedHandler;
            }
            catch { }
        }

        private void RefreshLocalizedStrings()
        {
            OnPropertyChanged(nameof(LocalizedTitle));
            OnPropertyChanged(nameof(LocalizedEnterUID));
            OnPropertyChanged(nameof(LocalizedUID));
            OnPropertyChanged(nameof(LocalizedUIDPlaceholder));
            OnPropertyChanged(nameof(LocalizedExpectedPublicKey));
            OnPropertyChanged(nameof(LocalizedPublicKeyPlaceholder));
            OnPropertyChanged(nameof(LocalizedSimulatedContact));
            OnPropertyChanged(nameof(LocalizedCancel));
            OnPropertyChanged(nameof(LocalizedSendRequest));
        }

        private async Task SendAsync()
        {
            if (IsSimulated)
            {
                // Simulated: skip network, add contact immediately
                try
                {
                    var input = (UID ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(input)) { Status = "UID required."; return; }
                    string uid;
                    string? name = null;
                    var idx = input.LastIndexOf('-');
                    if (idx > 0 && idx < input.Length - 1)
                    {
                        var right = input[(idx + 1)..];
                        if (IsUid(right))
                        {
                            uid = Trim(right);
                            name = input[..idx].Trim();
                        }
                        else
                        {
                            Status = "Invalid UID format."; return;
                        }
                    }
                    else
                    {
                        if (!IsUid(input)) { Status = "Invalid UID."; return; }
                        uid = Trim(input);
                    }
                    // Default display name for simulated contacts unless a custom name was provided
                    var display = !string.IsNullOrWhiteSpace(name) ? name! : uid;
                    var added = AppServices.Contacts.AddContact(new Models.Contact { UID = uid, DisplayName = display, ExpectedPublicKeyHex = null, IsSimulated = true }, AppServices.Passphrase);
                    if (added)
                    {
                        AppServices.Peers.IncludeContacts();
                        Status = "Simulated contact added.";
                        
                        // Force immediate contact list refresh to ensure UI updates
                        try
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                try { AppServices.Contacts.NotifyChanged(); } catch { }
                            }, Avalonia.Threading.DispatcherPriority.Background);
                        }
                        catch { }
                        
                        CloseRequested?.Invoke(true);
                    }
                    else
                    {
                        Status = "Contact already exists or invalid.";
                    }
                }
                catch { Status = "Failed to add simulated contact."; }
                finally { IsBusy = false; }
                return;
            }

            IsBusy = true; Status = "Requesting permission…";
            try
            {
                string? host = string.IsNullOrWhiteSpace(Host) ? null : Host;
                int? port = Port > 0 ? Port : null;
                var result = await AppServices.ContactRequests.SendRequestAsync(UID, host, port, TimeSpan.FromSeconds(20), ExpectedPublicKeyHex);
                switch (result)
                {
                    case Services.ContactRequestResult.Accepted:
                        Status = "Added!";
                        CloseRequested?.Invoke(true);
                        break;
                    case Services.ContactRequestResult.Rejected:
                        Status = "Request rejected.";
                        // Close too, since user explicitly rejected
                        CloseRequested?.Invoke(false);
                        break;
                    case Services.ContactRequestResult.NotFound:
                        Status = "Contact not found (no active peer).";
                        break;
                    case Services.ContactRequestResult.Timeout:
                        Status = "No response (timeout).";
                        break;
                    default:
                        Status = "Request failed.";
                        break;
                }
            }
            catch
            {
                Status = "Failed to send request.";
            }
            finally { IsBusy = false; }
        }

        private static string Trim(string uid)
        {
            var s = (uid ?? string.Empty).Trim();
            return s.StartsWith("usr-", StringComparison.Ordinal) && s.Length > 4 ? s.Substring(4) : s;
        }

        private static bool IsUid(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            // Normalize optional legacy prefix for validation
            if (s.StartsWith("usr-", StringComparison.Ordinal) && s.Length > 4) s = s.Substring(4);
            if (s.Contains('-')) return false;
            if (s.Length < 8) return false;
            foreach (var ch in s)
            {
                if (!char.IsLetterOrDigit(ch)) return false;
            }
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
