using System.Text.Json;
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.Contracts.Collection;

public sealed class OffsetPreservingDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) => reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value,
        JsonSerializerOptions options) => writer.WriteStringValue(value);
}
