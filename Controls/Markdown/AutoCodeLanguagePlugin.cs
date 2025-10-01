using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Markdown.Avalonia.Plugins;
using Markdown.Avalonia.SyntaxHigh;

namespace ZTalk.Controls.Markdown;

public sealed class AutoCodeLanguagePlugin : IMdAvPluginRequestAnother
{
    private SyntaxHighlight? _syntaxHighlight;

    public IEnumerable<Type> DependsOn => new[] { typeof(SyntaxHighlight) };

    public void Inject(IEnumerable<IMdAvPlugin> plugins)
    {
        _syntaxHighlight = plugins.OfType<SyntaxHighlight>().FirstOrDefault();
    }

    public void Setup(SetupInfo info)
    {
        if (_syntaxHighlight == null)
        {
            System.Diagnostics.Debug.WriteLine("[AutoCodeLanguagePlugin] SyntaxHighlight is null!");
            try { ZTalk.Utilities.LoggingPaths.TryWrite(System.IO.Path.Combine(ZTalk.Utilities.LoggingPaths.LogsDirectory, "syntax-plugin.log"), "SyntaxHighlight is null\n"); } catch {}
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[AutoCodeLanguagePlugin] Setting up Rust alias. Current aliases: {_syntaxHighlight.Aliases.Count}");
        // Also write an on-disk trace so we can observe plugin execution when Debug listeners are not attached.
        try
        {
            System.IO.Directory.CreateDirectory("logs");
            ZTalk.Utilities.LoggingPaths.TryWrite(System.IO.Path.Combine(ZTalk.Utilities.LoggingPaths.LogsDirectory, "syntax-plugin.log"), $"Setting up Rust alias. Current aliases: {_syntaxHighlight.Aliases.Count}\n");
        }
        catch { }
        EnsureAlias(_syntaxHighlight.Aliases, "rust", "avares://ZTalk/Assets/Syntax/Rust.xshd");
        System.Diagnostics.Debug.WriteLine($"[AutoCodeLanguagePlugin] After setup, aliases: {_syntaxHighlight.Aliases.Count}");
        try { ZTalk.Utilities.LoggingPaths.TryWrite(System.IO.Path.Combine(ZTalk.Utilities.LoggingPaths.LogsDirectory, "syntax-plugin.log"), $"After setup, aliases: {_syntaxHighlight.Aliases.Count}\n"); } catch { }
        
        foreach (var alias in _syntaxHighlight.Aliases)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoCodeLanguagePlugin] Alias: {alias.Name} -> {alias.XSHD}");
            try { ZTalk.Utilities.LoggingPaths.TryWrite(System.IO.Path.Combine(ZTalk.Utilities.LoggingPaths.LogsDirectory, "syntax-plugin.log"), $"Alias: {alias.Name} -> {alias.XSHD}\n"); } catch { }
        }
    }

    private static void EnsureAlias(ObservableCollection<Alias> aliases, string name, string resourceUri)
    {
        var targetUri = new Uri(resourceUri);
        var existing = aliases.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (existing.XSHD == null || !UriEquals(existing.XSHD, targetUri))
            {
                var index = aliases.IndexOf(existing);
                var replacement = new Alias
                {
                    Name = existing.Name,
                    XSHD = targetUri
                };
                if (index >= 0)
                {
                    aliases[index] = replacement;
                }
                else
                {
                    aliases.Remove(existing);
                    aliases.Add(replacement);
                }
            }
            return;
        }

        aliases.Add(new Alias
        {
            Name = name,
            XSHD = targetUri
        });
    }

    private static bool UriEquals(Uri? left, Uri right)
    {
        if (left is null)
        {
            return false;
        }

        return Uri.Compare(left, right, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }
}
