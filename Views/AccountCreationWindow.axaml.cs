/*
    Account creation window code-behind: Topmost, non-closable onboarding shell.
    - Hides Cancel when forced; delegates creation to ViewModel and shows passphrase modal.
*/
using Avalonia.Controls;

using P2PTalk.ViewModels;

namespace P2PTalk.Views
{
    public partial class AccountCreationWindow : Window
    {
    private bool _allowClose;
        public AccountCreationWindow()
        {
            InitializeComponent();
            var vm = new AccountCreationViewModel();
            DataContext = vm;
            // If no account exists, this is forced onboarding: disable cancel.
            vm.CanCancel = P2PTalk.Services.AppServices.Accounts.HasAccount();
            vm.CloseRequested += (_, __) => { _allowClose = true; Close(); };
            this.Closing += (s, e) => { if (!_allowClose) { e.Cancel = true; this.Topmost = true; } };
        }

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            try { if (DataContext is System.IDisposable d) d.Dispose(); } catch { }
            try { DataContext = null; } catch { }
        }
    }
}
