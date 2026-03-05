using System.Collections.Generic;

namespace Zer0Talk.Utilities
{
    public sealed class ReactionEmojiCategory
    {
        public string IconEmoji { get; init; } = "🙂";
        public string DisplayName { get; init; } = string.Empty;
        public IReadOnlyList<string> Emojis { get; init; } = new List<string>();
    }
}
