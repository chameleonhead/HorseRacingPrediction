using System.Text.Json;
using System.Text.Json.Serialization;

namespace HorseRacingPrediction.ApiClient;

internal sealed class NumericRequestFlexibleResponseEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(NumericRequestFlexibleResponseEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
