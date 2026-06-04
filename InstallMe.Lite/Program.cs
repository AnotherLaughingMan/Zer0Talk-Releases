using System;
using System.Windows.Forms;

namespace InstallMe.Lite
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var config = InstallerConfig.Load(args ?? Array.Empty<string>());
            var launchOptions = InstallerLaunchOptions.Parse(args ?? Array.Empty<string>());
            Uninstaller.Configure(config);

            // Support a silent uninstall mode: InstallMe.Lite.exe /uninstall
            if (launchOptions.UninstallRequested)
            {
                // Try to perform an unattended uninstall using registry-stored install path
                try
                {
                    var code = Uninstaller.RunFromArgs();
                    Environment.Exit(code);
                }
                catch
                {
                    Environment.Exit(1);
                }
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(config, launchOptions));
        }
    }
}