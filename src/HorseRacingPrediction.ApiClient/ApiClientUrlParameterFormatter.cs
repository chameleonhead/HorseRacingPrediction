using System.Globalization;
using System.Reflection;
using Refit;

namespace HorseRacingPrediction.ApiClient;

internal sealed class ApiClientUrlParameterFormatter : IUrlParameterFormatter
{
    public string? Format(object? value, ICustomAttributeProvider attributeProvider, Type declaredType) => value switch
    {
        null => null,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        Enum enumValue => Convert.ToInt64(enumValue, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
