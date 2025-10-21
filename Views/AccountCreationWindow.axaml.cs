/*
    Account creation window code-behind: Topmost, non-closable onboarding shell.
    - Hides Cancel when forced; delegates creation to ViewModel and shows passphrase modal.
*/
using Avalonia.Controls;

using Zer0Talk.ViewModels;

namespace Zer0Talk.Views
{
    public partial class AccountCreationWindow : Window
    {
    private bool _allowClose;
        
        private static void Log(string msg)
        {
            try
            {
                if (!Zer0Talk.Utilities.LoggingPaths.Enabled) return;
                var line = $"[{System.DateTime.Now:O}] {msg}";
                System.IO.File.AppendAllText(Zer0Talk.Utilities.LoggingPaths.AccountCreation, line + System.Environment.NewLine);
            }
            catch { }
        }
        
        public AccountCreationWindow()
        {
            try
            {
                Log("AccountCreationWindow.ctor.Start");
                
                InitializeComponent();
                Log("AccountCreationWindow.InitializeComponent.Done");
                
                var vm = new AccountCreationViewModel();
                Log("AccountCreationWindow.VM.Created");
                
                DataContext = vm;
                Log("AccountCreationWindow.DataContext.Set");
                
                // If no account exists, this is forced onboarding: disable cancel.
                vm.CanCancel = Zer0Talk.Services.AppServices.Accounts.HasAccount();
                Log($"AccountCreationWindow.CanCancel.Set={vm.CanCancel}");
                
                vm.CloseRequested += (_, __) => { Log("AccountCreationWindow.CloseRequested"); _allowClose = true; Close(); };
                this.Closing += (s, e) => { Log($"AccountCreationWindow.Closing allowClose={_allowClose}"); if (!_allowClose) { e.Cancel = true; this.Topmost = true; } };
                this.Closed += (s, e) => { Log("AccountCreationWindow.Closed"); };
                
                Log("AccountCreationWindow.ctor.Complete");
            }
            catch (System.Exception ex)
            {
                Log($"AccountCreationWindow.ctor.Error: {ex.GetType().Name} - {ex.Message}");
                Zer0Talk.Utilities.ErrorLogger.LogException(ex, source: "AccountCreationWindow.ctor.Error");
                throw;
            }
        }

        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            try { if (DataContext is System.IDisposable d) d.Dispose(); } catch { }
            try { DataContext = null; } catch { }
        }
    }
}
