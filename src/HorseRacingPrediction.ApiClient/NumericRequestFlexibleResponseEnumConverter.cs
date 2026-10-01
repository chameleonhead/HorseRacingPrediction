using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.ApiClient;

internal sealed class NumericRequestFlexibleResponseEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<TEnum>(reader.GetString(), ignoreCase: true, out var parsed))
            return parsed;

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number))
            return (TEnum)Enum.ToObject(typeof(TEnum), number);

        throw new JsonException($"Expected a string or numeric {typeof(TEnum).Name} value.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
}
