/*
    Account creation VM: collects profile fields and creates encrypted account.
    - Triggers modal passphrase dialog with Copy/Save on success.
*/
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

using Zer0Talk.Services;

namespace Zer0Talk.ViewModels
{
    public class AccountCreationViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? CloseRequested;

        private string _displayName = string.Empty;
        private string _username = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _canCancel = true;

        public string DisplayName { get => _displayName; set { _displayName = value; OnPropertyChanged(); } }
        public string Username { get => _username; set { _username = value; OnPropertyChanged(); RefreshCommands(); } }
        public string ErrorMessage { get => _errorMessage; set { _errorMessage = value; OnPropertyChanged(); } }
        public bool CanCancel { get => _canCancel; set { _canCancel = value; OnPropertyChanged(); } }

        public ICommand CreateCommand { get; }
        public ICommand CancelCommand { get; }
        // no DoneCommand; closing handled after modal dialog

        // Localized strings
        public string LocalizedTitle => Services.AppServices.Localization.GetString("AccountCreation.Title", "Create Account");
        public string LocalizedCreateLocalAccount => Services.AppServices.Localization.GetString("AccountCreation.CreateLocalAccount", "Create your local account");
        public string LocalizedOnTop => Services.AppServices.Localization.GetString("AccountCreation.OnTop", "On top");
        public string LocalizedKeepOnTop => Services.AppServices.Localization.GetString("AccountCreation.KeepOnTop", "Keep this window on top of other windows");
        public string LocalizedPickUsername => Services.AppServices.Localization.GetString("AccountCreation.PickUsername", "Pick a username (6–24 chars). We'll generate a secure keypair and UID.");
        public string LocalizedDisplayName => Services.AppServices.Localization.GetString("AccountCreation.DisplayName", "Display name");
        public string LocalizedUsername => Services.AppServices.Localization.GetString("AccountCreation.Username", "Username");
        public string LocalizedCancel => Services.AppServices.Localization.GetString("AccountCreation.Cancel", "Cancel");
        public string LocalizedCreate => Services.AppServices.Localization.GetString("AccountCreation.Create", "Create");

        public AccountCreationViewModel()
        {
            CreateCommand = new RelayCommand(async _ => await CreateAsync(), _ => !string.IsNullOrWhiteSpace(Username));
            CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, EventArgs.Empty));
            // no DoneCommand

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
            OnPropertyChanged(nameof(LocalizedCreateLocalAccount));
            OnPropertyChanged(nameof(LocalizedOnTop));
            OnPropertyChanged(nameof(LocalizedKeepOnTop));
            OnPropertyChanged(nameof(LocalizedPickUsername));
            OnPropertyChanged(nameof(LocalizedDisplayName));
            OnPropertyChanged(nameof(LocalizedUsername));
            OnPropertyChanged(nameof(LocalizedCancel));
            OnPropertyChanged(nameof(LocalizedCreate));
        }

        private async Task CreateAsync()
        {
            try
            {
                ErrorMessage = string.Empty;
                var pass = await AppServices.Accounts.EnsureAccountAsync(() => Task.FromResult((DisplayName, Username)));
                if (!string.IsNullOrEmpty(pass))
                {
                    // Set passphrase now so any immediate container operations use the right key
                    AppServices.Passphrase = pass;
                    // Show modal passphrase dialog with copy/save options
                    await AppServices.Dialogs.ShowPassphraseDialogAsync(pass);
                }
                // Close regardless (account is created if passphrase was returned or already existed)
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void RefreshCommands()
        {
            (CreateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }
}
