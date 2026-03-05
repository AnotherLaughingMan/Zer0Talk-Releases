using System;

namespace Zer0Talk.Utilities
{
    public readonly record struct ReactionCommandParameter(Guid MessageId, string Emoji);
}
