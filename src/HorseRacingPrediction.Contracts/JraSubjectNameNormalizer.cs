using System.Text;
using System.Text.RegularExpressions;

namespace HorseRacingPrediction.Contracts;

public static partial class JraSubjectNameNormalizer
{
    public static string CanonicalizeDisplayName(string subjectType, string value)
    {
        var canonical = value.Normalize(NormalizationForm.FormKC).Trim();
        if (subjectType is "Trainer" or "Jockey")
            canonical = AffiliationSuffix().Replace(canonical, string.Empty).Trim();
        if (subjectType == "Horse")
            canonical = HorseRegistrationMark().Replace(canonical, string.Empty).Trim();
        return canonical;
    }

    public static string NormalizeIdentityName(string subjectType, string value) =>
        Whitespace().Replace(CanonicalizeDisplayName(subjectType, value), string.Empty);

    [GeneratedRegex(@"\s*[（(][^）)]*[）)]\s*$")]
    private static partial Regex AffiliationSuffix();

    [GeneratedRegex(@"^(?:マルガイ|マルチ|マル外|マル地)\s*")]
    private static partial Regex HorseRegistrationMark();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
