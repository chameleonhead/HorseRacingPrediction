using System.Security.Cryptography;
using System.Text;

namespace HorseRacingPrediction.ApiClient;

public static class OwnerIdentityContract
{
    public static string NormalizeName(string value) => value.Normalize(NormalizationForm.FormKC)
        .Replace("株式会社", "", StringComparison.Ordinal)
        .Replace("（株）", "", StringComparison.Ordinal)
        .Replace("(株)", "", StringComparison.Ordinal)
        .Replace(" ", "", StringComparison.Ordinal)
        .Replace("　", "", StringComparison.Ordinal)
        .Trim()
        .ToUpperInvariant();

    public static string CreateId(string ownerName) =>
        DeterministicIdGenerator.BuildEntityId("owner", NormalizeName(ownerName));

    public static string CreateLegacyId(string ownerName)
    {
        var normalized = NormalizeName(ownerName);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..20]
            .ToLowerInvariant();
    }
}
