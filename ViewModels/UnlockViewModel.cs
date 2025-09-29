/*
    Unlock VM: validates passphrase against account; loads settings and contacts on success.
    - Supports lost passphrase recovery by rotating credentials.
*/
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

using P2PTalk.Services;

namespace P2PTalk.ViewModels
{
    public class UnlockViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? CloseRequested;

        private string _passphrase = string.Empty;
        private string _errorMessage = string.Empty;
    private bool _isRecovery;
        private string _recoveryUsername = string.Empty;
        private string _newPassphrase = string.Empty;
    private bool _hasNewPassphrase;
    private bool _rememberPassphrase;

        public string Passphrase { get => _passphrase; set { _passphrase = value; OnPropertyChanged(); (UnlockCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }
        public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }
        public bool IsRecovery { get => _isRecovery; set { _isRecovery = value; OnPropertyChanged(); } }
        public string RecoveryUsername { get => _recoveryUsername; set { _recoveryUsername = value; OnPropertyChanged(); } }
        public string NewPassphrase { get => _newPassphrase; set { _newPassphrase = value; OnPropertyChanged(); } }
        public bool HasNewPassphrase { get => _hasNewPassphrase; set { _hasNewPassphrase = value; OnPropertyChanged(); } }
        public bool RememberPassphrase { get => _rememberPassphrase; set { _rememberPassphrase = value; OnPropertyChanged(); } }

        public ICommand UnlockCommand { get; }
    public ICommand LostPassphraseCommand { get; }
        public ICommand CancelRecoveryCommand { get; }
        public ICommand ConfirmRecoveryCommand { get; }
        public ICommand AcceptNewPassphraseCommand { get; }

        public UnlockViewModel()
        {
            UnlockCommand = new RelayCommand(async _ => await UnlockAsync(), _ => CanUnlock());
            LostPassphraseCommand = new RelayCommand(_ => { /* now handled in code-behind launching dialog */ });
            CancelRecoveryCommand = new RelayCommand(_ => { IsRecovery = false; ErrorMessage = string.Empty; RecoveryUsername = ""; });
            ConfirmRecoveryCommand = new RelayCommand(async _ => await RecoverAsync());
            AcceptNewPassphraseCommand = new RelayCommand(_ => { if (HasNewPassphrase) CloseRequested?.Invoke(this, EventArgs.Empty); });
            try
            {
                // Prefer preference sidecar at startup so we can reflect checkbox state pre-unlock
                RememberPassphrase = AppServices.Settings.GetRememberPreference();
            }
            catch { }
        }

        private bool CanUnlock() => !string.IsNullOrWhiteSpace(Passphrase);

        private async Task UnlockAsync()
        {
            try
            {
                // Normalize and validate passphrase against the account container
                var input = (Passphrase ?? string.Empty).Trim();
                var account = AppServices.Accounts.LoadAccount(input);
                AppServices.Passphrase = input;
                // Load identity keys into memory
                AppServices.Identity.LoadFromAccount(account);

                // Load settings; if decryption fails (e.g., legacy file created with dev key), reset to defaults
                try { AppServices.Settings.Load(AppServices.Passphrase); }
                catch { AppServices.Settings.ResetToDefaults(AppServices.Passphrase); }

                // Load contacts (non-fatal if it fails; service logs internally)
                AppServices.Contacts.Load(AppServices.Passphrase);

                // Ensure display name persists in account and is reflected into settings if settings has none
                try
                {
                    if (string.IsNullOrWhiteSpace(AppServices.Settings.Settings.DisplayName) && !string.IsNullOrWhiteSpace(account.DisplayName))
                    {
                        AppServices.Settings.Settings.DisplayName = account.DisplayName;
                        AppServices.Settings.Save(AppServices.Passphrase);
                    }
                }
                catch { }

                // Persist remembered passphrase if requested (or preference was kept but secret purged)
                if (RememberPassphrase)
                {
                    // Always persist current passphrase and preference
                    AppServices.Settings.SetRememberedPassphrase(input);
                    AppServices.Settings.Settings.RememberPassphrase = true;
                    AppServices.Settings.Save(AppServices.Passphrase);
                }

                // Apply theme immediately so Unlock reflects Light/Sandy/Butter before closing
                AppServices.Theme.SetTheme(AppServices.Settings.Settings.Theme);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Render);

                // Signal lock service via Passphrase being non-empty; then request window close
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                ErrorMessage = "Invalid passphrase.";
                try { AppServices.Passphrase = string.Empty; } catch { }
            }
            return;
        }

        private async Task RecoverAsync()
        {
            // Warning + confirmation via dialog
            var ok = await AppServices.Dialogs.ConfirmAsync(
                "Lost Passphrase Recovery",
                "This will generate a NEW passphrase and re-encrypt your data. Continue?",
                "Proceed",
                "Cancel");
            if (!ok) return;

            try
            {
                var newPass = await AppServices.Accounts.RecoverLostPassphraseAsync(() => Task.FromResult(true));
                NewPassphrase = newPass;
                HasNewPassphrase = true;
                IsRecovery = false;
                ErrorMessage = string.Empty;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
