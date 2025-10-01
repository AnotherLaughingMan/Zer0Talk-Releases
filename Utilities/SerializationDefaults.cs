using System.Text.Json;

namespace ZTalk.Utilities;

public static class SerializationDefaults
{
    public static JsonSerializerOptions Compact { get; } = Create(writeIndented: false);

    public static JsonSerializerOptions Indented { get; } = Create(writeIndented: true);

    private static JsonSerializerOptions Create(bool writeIndented)
    {
        return new JsonSerializerOptions
        {
            WriteIndented = writeIndented
        };
    }
}
