namespace HorseRacingPrediction.Scraping.JraNavigation;

/// <summary>
/// JRA の性齢表記を正規化して性別コードと年齢を抽出する。
/// </summary>
public static class JraSexAgeParser
{
    public static (string? SexCode, int? Age) Parse(string? sexAge)
    {
        if (string.IsNullOrWhiteSpace(sexAge))
        {
            return (null, null);
        }

        var normalized = sexAge.Trim();
        var sexCode = ParseSexCode(normalized);

        var ageDigits = new string(normalized.Where(char.IsDigit).ToArray());
        int? age = int.TryParse(ageDigits, out var parsedAge) ? parsedAge : null;

        return (sexCode, age);
    }

    private static string? ParseSexCode(string value)
    {
        if (value.StartsWith("せん", StringComparison.Ordinal)
            || value.StartsWith("セン", StringComparison.Ordinal))
        {
            return "G";
        }

        return value[0] switch
        {
            '牡' or 'M' or 'm' => "M",
            '牝' or 'F' or 'f' => "F",
            'セ' or 'せ' or '騙' or 'G' or 'g' or 'C' or 'c' => "G",
            _ => null
        };
    }
}
