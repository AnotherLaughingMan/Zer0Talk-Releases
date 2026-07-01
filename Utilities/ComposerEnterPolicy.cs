using Avalonia.Input;

namespace Zer0Talk.Utilities;

public static class ComposerEnterPolicy
{
    public static bool ShouldSendOnKeyPress(Key key, KeyModifiers modifiers)
    {
        if (key != Key.Enter)
            return false;

        // Only plain Enter sends; modified Enter should not trigger send.
        return modifiers == KeyModifiers.None;
    }
}
