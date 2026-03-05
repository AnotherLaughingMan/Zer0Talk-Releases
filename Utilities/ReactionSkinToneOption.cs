namespace Zer0Talk.Utilities
{
    public sealed class ReactionSkinToneOption
    {
        public string Name { get; init; } = string.Empty;
        public string DisplayEmoji { get; init; } = string.Empty;
        public string Modifier { get; init; } = string.Empty;

        public override string ToString()
            => string.IsNullOrWhiteSpace(DisplayEmoji) ? Name : $"{DisplayEmoji} {Name}";
    }
}
