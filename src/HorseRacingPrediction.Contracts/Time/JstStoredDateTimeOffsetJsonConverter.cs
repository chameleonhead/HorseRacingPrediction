using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Time;

public sealed class JstStoredDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("日時が指定されていません。");
        return JstTime.ParseStoredValue(text);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(JstTime.ToDatabaseString(value));
}
