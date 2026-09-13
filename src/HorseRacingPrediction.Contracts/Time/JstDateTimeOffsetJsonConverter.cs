using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Time;

public sealed class JstDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("日時が指定されていません。");
        if (!HasOffset(text)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
            throw new JsonException("日時はoffset付きISO 8601形式で指定してください。");

        return JstTime.Convert(value);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(JstTime.Convert(value).ToString("O", CultureInfo.InvariantCulture));

    private static bool HasOffset(string value) =>
        value.EndsWith('Z') || value.LastIndexOf('+') > 9 || value.LastIndexOf('-') > 9;
}
