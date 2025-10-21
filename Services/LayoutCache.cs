/*
    LayoutCache: lightweight, local-only persistence for window geometry.
    - Stores size/position/state in %APPDATA%\Zer0Talk\window_state.json.
    - Avoids frequent writes to settings.p2e and keeps layout separate from configuration.
    - Safe no-throw API; failures silently fall back to defaults.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Zer0Talk.Utilities;

namespace Zer0Talk.Services
{
    internal static class LayoutCache
    {
        private static readonly object _gate = new();
        private static string GetPath()
        {
            var path = Path.Combine(Zer0Talk.Utilities.AppDataPaths.Root, "window_state.json");
            return path;
        }

    internal sealed record WindowLayout(double? Width, double? Height, double? X, double? Y, int? State);

        private sealed class Store
        {
            public Dictionary<string, WindowLayout> Windows { get; set; } = new();
        }

        public static WindowLayout? Load(string key)
        {
            try
            {
                lock (_gate)
                {
                    var path = GetPath();
                    if (!File.Exists(path)) return null;
                    var json = File.ReadAllText(path);
                    var s = JsonSerializer.Deserialize<Store>(json);
                    if (s?.Windows != null && s.Windows.TryGetValue(key, out var layout))
                        return layout;
                    return null;
                }
            }
            catch { return null; }
        }

        public static void Save(string key, WindowLayout layout)
        {
            try
            {
                lock (_gate)
                {
                    var path = GetPath();
                    var dir = Path.GetDirectoryName(path)!;
                    Directory.CreateDirectory(dir);
                    Store store;
                    if (File.Exists(path))
                    {
                        try { store = JsonSerializer.Deserialize<Store>(File.ReadAllText(path)) ?? new Store(); }
                        catch { store = new Store(); }
                    }
                    else store = new Store();
                    store.Windows[key] = layout;
                    var json = JsonSerializer.Serialize(store, SerializationDefaults.Indented);
                    File.WriteAllText(path, json);
                }
            }
            catch { }
        }
    }
}

