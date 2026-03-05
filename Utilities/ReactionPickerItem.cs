namespace Zer0Talk.Utilities
{
    public sealed class ReactionPickerItem
    {
        public string Value { get; init; } = string.Empty;
        public string? FlagCode { get; init; }
        public string? Tooltip { get; init; }
        public bool IsFlag => !string.IsNullOrWhiteSpace(FlagCode);
    }
}
