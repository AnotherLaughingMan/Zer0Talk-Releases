using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Zer0Talk.RelayServer.Services;

public enum RelayFirewallRuleRefreshResult
{
    UpToDate,
    Success,
    Canceled,
    Failed,
    SkippedNonWindows,
    MissingExecutablePath
}

public static class RelayWindowsFirewallRuleManager
{
    private static readonly object Gate = new();
    private static bool _startupAttempted;

    private static string StateFilePath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, "Zer0TalkRelay", "firewall-sync.state");
        }
    }

    public static Task<RelayFirewallRuleRefreshResult> EnsureRulesForCurrentBuildAsync(
        int relayPort,
        int discoveryPort,
        bool federationEnabled,
        int federationPort)
    {
        lock (Gate)
        {
            if (_startupAttempted)
            {
                return Task.FromResult(RelayFirewallRuleRefreshResult.UpToDate);
            }

            _startupAttempted = true;
        }

        return RefreshRulesAsync(relayPort, discoveryPort, federationEnabled, federationPort, force: false);
    }

    public static Task<RelayFirewallRuleRefreshResult> RefreshRulesAsync(
        int relayPort,
        int discoveryPort,
        bool federationEnabled,
        int federationPort,
        bool force)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(RelayFirewallRuleRefreshResult.SkippedNonWindows);
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            try { exePath = Process.GetCurrentProcess().MainModule?.FileName; } catch { }
        }

        if (string.IsNullOrWhiteSpace(exePath))
        {
            return Task.FromResult(RelayFirewallRuleRefreshResult.MissingExecutablePath);
        }

        var normalizedRelayPort = relayPort is >= 1 and <= 65535 ? relayPort : 443;
        var normalizedDiscoveryPort = discoveryPort is >= 1 and <= 65535 ? discoveryPort : 38384;
        var normalizedFederationPort = federationPort is >= 1 and <= 65535 ? federationPort : 8443;

        if (!force && !NeedsRefresh(exePath, normalizedRelayPort, normalizedDiscoveryPort, federationEnabled, normalizedFederationPort))
        {
            return Task.FromResult(RelayFirewallRuleRefreshResult.UpToDate);
        }

        return ExecuteRefreshAsync(exePath, normalizedRelayPort, normalizedDiscoveryPort, federationEnabled, normalizedFederationPort);
    }

    private static bool NeedsRefresh(string exePath, int relayPort, int discoveryPort, bool federationEnabled, int federationPort)
    {
        try
        {
            if (!File.Exists(StateFilePath)) return true;
            var lines = File.ReadAllLines(StateFilePath);
            if (lines.Length < 5) return true;

            var savedPath = lines[0];
            var savedRelayPort = int.TryParse(lines[1], out var parsedRelay) ? parsedRelay : 0;
            var savedDiscoveryPort = int.TryParse(lines[2], out var parsedDiscovery) ? parsedDiscovery : 0;
            var savedFederationEnabled = bool.TryParse(lines[3], out var parsedFederationEnabled) && parsedFederationEnabled;
            var savedFederationPort = int.TryParse(lines[4], out var parsedFederationPort) ? parsedFederationPort : 0;

            return !string.Equals(savedPath, exePath, StringComparison.OrdinalIgnoreCase)
                || savedRelayPort != relayPort
                || savedDiscoveryPort != discoveryPort
                || savedFederationEnabled != federationEnabled
                || savedFederationPort != federationPort;
        }
        catch
        {
            return true;
        }
    }

    private static void PersistState(string exePath, int relayPort, int discoveryPort, bool federationEnabled, int federationPort)
    {
        try
        {
            var dir = Path.GetDirectoryName(StateFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllLines(
                StateFilePath,
                new[]
                {
                    exePath,
                    relayPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    discoveryPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    federationEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    federationPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
        }
        catch
        {
        }
    }

    private static async Task<RelayFirewallRuleRefreshResult> ExecuteRefreshAsync(
        string exePath,
        int relayPort,
        int discoveryPort,
        bool federationEnabled,
        int federationPort)
    {
        try
        {
            var escapedExe = exePath.Replace("'", "''", StringComparison.Ordinal);
            var script = new StringBuilder();
            script.Append("$ErrorActionPreference='SilentlyContinue';");
            script.Append("$exe='").Append(escapedExe).Append("';");
            script.Append("$relayPort=").Append(relayPort.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            script.Append("$discoveryPort=").Append(discoveryPort.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            script.Append("$federationEnabled=").Append(federationEnabled ? "$true" : "$false").Append(';');
            script.Append("$federationPort=").Append(federationPort.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            script.Append("Get-NetFirewallRule -Group 'Zer0Talk Relay' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -Confirm:$false;");
            script.Append("$names=@('Zer0Talk Relay Inbound (App)','Zer0Talk Relay Outbound (App)','Zer0Talk Relay Inbound TCP','Zer0Talk Relay Inbound UDP Discovery','Zer0Talk Relay Federation Inbound TCP');");
            script.Append("foreach ($n in $names) { Get-NetFirewallRule -DisplayName $n -ErrorAction SilentlyContinue | Remove-NetFirewallRule -Confirm:$false };");
            script.Append("New-NetFirewallRule -DisplayName 'Zer0Talk Relay Inbound (App)' -Group 'Zer0Talk Relay' -Direction Inbound -Program $exe -Action Allow -Profile Any | Out-Null;");
            script.Append("New-NetFirewallRule -DisplayName 'Zer0Talk Relay Outbound (App)' -Group 'Zer0Talk Relay' -Direction Outbound -Program $exe -Action Allow -Profile Any | Out-Null;");
            script.Append("New-NetFirewallRule -DisplayName 'Zer0Talk Relay Inbound TCP' -Group 'Zer0Talk Relay' -Direction Inbound -Protocol TCP -LocalPort $relayPort -Action Allow -Profile Any | Out-Null;");
            script.Append("New-NetFirewallRule -DisplayName 'Zer0Talk Relay Inbound UDP Discovery' -Group 'Zer0Talk Relay' -Direction Inbound -Protocol UDP -LocalPort $discoveryPort -Action Allow -Profile Any | Out-Null;");
            script.Append("if ($federationEnabled -and $federationPort -gt 0 -and $federationPort -ne $relayPort) { New-NetFirewallRule -DisplayName 'Zer0Talk Relay Federation Inbound TCP' -Group 'Zer0Talk Relay' -Direction Inbound -Protocol TCP -LocalPort $federationPort -Action Allow -Profile Any | Out-Null; }");

            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoLogo -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc == null) return RelayFirewallRuleRefreshResult.Failed;

            await Task.Run(() => proc.WaitForExit()).ConfigureAwait(false);
            if (proc.ExitCode == 0)
            {
                PersistState(exePath, relayPort, discoveryPort, federationEnabled, federationPort);
                return RelayFirewallRuleRefreshResult.Success;
            }

            return RelayFirewallRuleRefreshResult.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return RelayFirewallRuleRefreshResult.Canceled;
        }
        catch
        {
            return RelayFirewallRuleRefreshResult.Failed;
        }
    }
}
