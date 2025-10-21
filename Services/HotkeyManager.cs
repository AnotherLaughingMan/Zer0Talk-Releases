/*
    Centralized hotkey management service.
    - Registers and executes global application hotkeys
    - Supports user-configurable key combinations
    - Conflict detection and validation
    - Thread-safe registration and execution
*/
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;

namespace Zer0Talk.Services
{
    public class HotkeyManager
    {
        private static readonly object _lock = new object();
        private readonly Dictionary<string, HotkeyRegistration> _registrations = new();

        public static HotkeyManager Instance { get; } = new HotkeyManager();

        private HotkeyManager() { }

        /// <summary>
        /// Registers a hotkey action with the specified ID, key combination, and callback.
        /// </summary>
        public void Register(string id, Key key, KeyModifiers modifiers, Action callback, string? description = null)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(callback);

            lock (_lock)
            {
                _registrations[id] = new HotkeyRegistration
                {
                    Id = id,
                    Key = key,
                    Modifiers = modifiers,
                    Callback = callback,
                    Description = description ?? id
                };
            }
        }

        /// <summary>
        /// Unregisters a hotkey by ID.
        /// </summary>
        public void Unregister(string id)
        {
            lock (_lock)
            {
                _registrations.Remove(id);
            }
        }

        /// <summary>
        /// Updates an existing hotkey registration with a new key combination.
        /// </summary>
        public bool UpdateKeyBinding(string id, Key key, KeyModifiers modifiers)
        {
            lock (_lock)
            {
                if (_registrations.TryGetValue(id, out var registration))
                {
                    // Check for conflicts with other hotkeys
                    var conflict = _registrations.Values
                        .FirstOrDefault(r => r.Id != id && r.Key == key && r.Modifiers == modifiers);
                    
                    if (conflict != null)
                    {
                        return false; // Conflict detected
                    }

                    registration.Key = key;
                    registration.Modifiers = modifiers;
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Handles a key event and executes matching hotkey callback.
        /// Returns true if a hotkey was executed.
        /// </summary>
        public bool HandleKeyEvent(KeyEventArgs e)
        {
            if (e.Handled) return false;

            // Diagnostic: log incoming key event
            try
            {
                if (Zer0Talk.Utilities.LoggingPaths.Enabled)
                {
                    Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Hotkey] HandleKeyEvent received key={e.Key} mods={e.KeyModifiers}\n");
                }
            }
            catch { }

            lock (_lock)
            {
                // Tolerant match: require registration modifiers to be present in the event (allow extra modifiers)
                var match = _registrations.Values.FirstOrDefault(r =>
                    r.Key == e.Key && (e.KeyModifiers & r.Modifiers) == r.Modifiers);

                if (match != null)
                {
                    try
                    {
                        try
                        {
                            if (Zer0Talk.Utilities.LoggingPaths.Enabled)
                            {
                                Zer0Talk.Utilities.LoggingPaths.TryWrite(Zer0Talk.Utilities.LoggingPaths.UI, $"{DateTime.Now:O} [Hotkey] Matched registration id={match.Id} key={match.Key} mods={match.Modifiers}\n");
                            }
                        }
                        catch { }

                        match.Callback?.Invoke();
                        e.Handled = true;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Zer0Talk.Utilities.Logger.Log($"Hotkey execution error ({match.Id}): {ex.Message}");
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Gets all registered hotkeys.
        /// </summary>
        public IReadOnlyList<HotkeyInfo> GetAllHotkeys()
        {
            lock (_lock)
            {
                return _registrations.Values.Select(r => new HotkeyInfo
                {
                    Id = r.Id,
                    Key = r.Key,
                    Modifiers = r.Modifiers,
                    Description = r.Description
                }).ToList();
            }
        }

        /// <summary>
        /// Gets a specific hotkey registration.
        /// </summary>
        public HotkeyInfo? GetHotkey(string id)
        {
            lock (_lock)
            {
                if (_registrations.TryGetValue(id, out var registration))
                {
                    return new HotkeyInfo
                    {
                        Id = registration.Id,
                        Key = registration.Key,
                        Modifiers = registration.Modifiers,
                        Description = registration.Description
                    };
                }
                return null;
            }
        }

        /// <summary>
        /// Checks if a key combination conflicts with existing hotkeys.
        /// </summary>
        public bool HasConflict(Key key, KeyModifiers modifiers, string? excludeId = null)
        {
            lock (_lock)
            {
                return _registrations.Values.Any(r =>
                    (excludeId == null || r.Id != excludeId) &&
                    r.Key == key &&
                    r.Modifiers == modifiers);
            }
        }

        /// <summary>
        /// Formats a key combination as a human-readable string.
        /// </summary>
        public static string FormatKeyBinding(Key key, KeyModifiers modifiers)
        {
            var parts = new List<string>();

            if ((modifiers & KeyModifiers.Control) == KeyModifiers.Control)
                parts.Add("Ctrl");
            if ((modifiers & KeyModifiers.Alt) == KeyModifiers.Alt)
                parts.Add("Alt");
            if ((modifiers & KeyModifiers.Shift) == KeyModifiers.Shift)
                parts.Add("Shift");
            if ((modifiers & KeyModifiers.Meta) == KeyModifiers.Meta)
                parts.Add("Win");

            parts.Add(key.ToString());

            return string.Join("+", parts);
        }

        private class HotkeyRegistration
        {
            public string Id { get; set; } = string.Empty;
            public Key Key { get; set; }
            public KeyModifiers Modifiers { get; set; }
            public Action? Callback { get; set; }
            public string Description { get; set; } = string.Empty;
        }
    }

    public class HotkeyInfo
    {
        public string Id { get; set; } = string.Empty;
        public Key Key { get; set; }
        public KeyModifiers Modifiers { get; set; }
        public string Description { get; set; } = string.Empty;

        public string DisplayText => HotkeyManager.FormatKeyBinding(Key, Modifiers);
    }
}

