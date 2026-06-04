using System;

namespace InstallMe.Lite
{
    public sealed class InstallerLaunchOptions
    {
        public bool UninstallRequested { get; private set; }
        public bool Silent { get; private set; }
        public bool RestartAfterInstall { get; private set; }
        public bool ForceUpdateMode { get; private set; }
        public string? InstallPathOverride { get; private set; }

        public static InstallerLaunchOptions Parse(string[] args)
        {
            var options = new InstallerLaunchOptions();
            if (args == null || args.Length == 0)
            {
                return options;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i]?.Trim();
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                if (EqualsAny(arg, "/uninstall", "--uninstall"))
                {
                    options.UninstallRequested = true;
                    continue;
                }

                if (EqualsAny(arg, "/silent", "--silent", "/verysilent", "--verysilent"))
                {
                    options.Silent = true;
                    continue;
                }

                if (EqualsAny(arg, "/restart", "--restart", "--relaunch"))
                {
                    options.RestartAfterInstall = true;
                    continue;
                }

                if (EqualsAny(arg, "/update", "--update"))
                {
                    options.ForceUpdateMode = true;
                    continue;
                }

                if (EqualsAny(arg, "/mode", "--mode"))
                {
                    if (TryGetValue(args, ref i, out var modeValue) &&
                        modeValue.Equals("update", StringComparison.OrdinalIgnoreCase))
                    {
                        options.ForceUpdateMode = true;
                    }
                    continue;
                }

                if (EqualsAny(arg, "/installpath", "--install-path", "--target"))
                {
                    if (TryGetValue(args, ref i, out var pathValue) && !string.IsNullOrWhiteSpace(pathValue))
                    {
                        options.InstallPathOverride = pathValue.Trim();
                    }
                }
            }

            return options;
        }

        private static bool TryGetValue(string[] args, ref int index, out string value)
        {
            value = string.Empty;
            if (index + 1 >= args.Length)
            {
                return false;
            }

            var candidate = args[index + 1]?.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                index += 1;
                return false;
            }

            value = candidate;
            index += 1;
            return true;
        }

        private static bool EqualsAny(string value, params string[] options)
        {
            foreach (var option in options)
            {
                if (value.Equals(option, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
