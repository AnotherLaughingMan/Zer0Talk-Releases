using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Zer0Talk.Utilities;

namespace Zer0Talk.Services;

public enum FirewallRuleRefreshResult
{
    UpToDate,
    Success,
    Canceled,
    Failed,
    SkippedNonWindows,
    MissingExecutablePath
}

public static class WindowsFirewallRuleManager
{
    private const int DefaultPort = 26264;
    private const int DiscoveryUdpPort = 38384;
    private static readonly object _gate = new();
    private static bool _startupAttempted;

    private static string StateFilePath => AppDataPaths.Combine("firewall-sync.state");

    public static Task<FirewallRuleRefreshResult> EnsureRulesForCurrentBuildAsync(int configuredPort)
    {
        lock (_gate)
        {
            if (_startupAttempted)
            {
                return Task.FromResult(FirewallRuleRefreshResult.UpToDate);
            }
            _startupAttempted = true;
        }

        return RefreshRulesAsync(configuredPort, force: false);
    }

    public static Task<FirewallRuleRefreshResult> RefreshRulesAsync(int configuredPort, bool force)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(FirewallRuleRefreshResult.SkippedNonWindows);
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            try { exePath = Process.GetCurrentProcess().MainModule?.FileName; } catch { }
        }

        if (string.IsNullOrWhiteSpace(exePath))
        {
            return Task.FromResult(FirewallRuleRefreshResult.MissingExecutablePath);
        }

        var normalizedPort = configuredPort > 0 ? configuredPort : DefaultPort;

        if (!force && !NeedsRefresh(exePath, normalizedPort))
        {
            return Task.FromResult(FirewallRuleRefreshResult.UpToDate);
        }

        return ExecuteRefreshAsync(exePath, normalizedPort);
    }

    private static bool NeedsRefresh(string exePath, int port)
    {
        try
        {
            if (!File.Exists(StateFilePath)) return true;
            var lines = File.ReadAllLines(StateFilePath);
            if (lines.Length < 2) return true;
            var savedPath = lines[0];
            var savedPort = int.TryParse(lines[1], out var p) ? p : 0;
            return !string.Equals(savedPath, exePath, StringComparison.OrdinalIgnoreCase) || savedPort != port;
        }
        catch
        {
            return true;
        }
    }

    private static void PersistState(string exePath, int port)
    {
        try
        {
            var dir = Path.GetDirectoryName(StateFilePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(StateFilePath, new[] { exePath, port.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
        catch { }
    }

    private static async Task<FirewallRuleRefreshResult> ExecuteRefreshAsync(string exePath, int port)
    {
        try
        {
            var escapedExe = exePath.Replace("'", "''", StringComparison.Ordinal);
            // Script removes ALL Zer0Talk firewall rules (by Group and by display name pattern)
            // before creating exactly 4 clean rules. This eliminates duplicates from:
            // - Previous troubleshooter runs
            // - Windows auto-generated "allow" prompt rules (which use the exe name as display name)
            // - Stale rules pointing at old install paths
            var script =
                "$ErrorActionPreference='SilentlyContinue';" +
                "$exe='" + escapedExe + "';" +
                "$port=" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" +
                "$udp=" + DiscoveryUdpPort.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" +
                // Remove by Group first (catches all rules we created)
                "Get-NetFirewallRule -Group 'Zer0Talk' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -Confirm:$false;" +
                // Remove by explicit names (catches legacy rules from older versions)
                "$names=@('Zer0Talk Inbound (App)','Zer0Talk Outbound (App)','Zer0Talk Inbound TCP','Zer0Talk Inbound UDP Discovery','Zer0Talk Inbound','Zer0Talk Outbound');" +
                "foreach ($n in $names) { Get-NetFirewallRule -DisplayName $n -ErrorAction SilentlyContinue | Remove-NetFirewallRule -Confirm:$false } ;" +
                // Remove Windows auto-generated prompt rules (display name matches exe name without extension)
                "Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like 'Zer0Talk*' -or $_.DisplayName -like '*Zer0Talk.exe*' } | Remove-NetFirewallRule -Confirm:$false;" +
                // Create exactly 4 clean rules
                "New-NetFirewallRule -DisplayName 'Zer0Talk Inbound (App)' -Group 'Zer0Talk' -Direction Inbound -Program $exe -Action Allow -Profile Any | Out-Null;" +
                "New-NetFirewallRule -DisplayName 'Zer0Talk Outbound (App)' -Group 'Zer0Talk' -Direction Outbound -Program $exe -Action Allow -Profile Any | Out-Null;" +
                "New-NetFirewallRule -DisplayName 'Zer0Talk Inbound TCP' -Group 'Zer0Talk' -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow -Profile Any | Out-Null;" +
                "New-NetFirewallRule -DisplayName 'Zer0Talk Inbound UDP Discovery' -Group 'Zer0Talk' -Direction Inbound -Protocol UDP -LocalPort $udp -Action Allow -Profile Any | Out-Null;";

            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc == null) return FirewallRuleRefreshResult.Failed;

            await Task.Run(() => proc.WaitForExit());
            if (proc.ExitCode == 0)
            {
                PersistState(exePath, port);
                return FirewallRuleRefreshResult.Success;
            }

            return FirewallRuleRefreshResult.Failed;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return FirewallRuleRefreshResult.Canceled;
        }
        catch (Exception ex)
        {
            Logger.Log($"Firewall rule refresh failed: {ex.Message}");
            return FirewallRuleRefreshResult.Failed;
        }
    }
}