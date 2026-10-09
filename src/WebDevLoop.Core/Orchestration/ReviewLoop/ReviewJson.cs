using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>Snake-case JSON matching the report tools' wire format, used for persisted results and prompt payloads.</summary>
internal static class ReviewJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    public static JsonSerializerOptions Indented { get; } = new(Options) { WriteIndented = true };

    /// <summary>The wire name of an enum value, e.g. <c>coding_standards</c>.</summary>
    public static string Name<TEnum>(TEnum value)
        where TEnum : struct, Enum => JsonSerializer.Serialize(value, Options).Trim('"');
}
