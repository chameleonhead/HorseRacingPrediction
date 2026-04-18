namespace HorseRacingPrediction.Api.Security;

public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKey";

    public string HeaderName { get; set; } = "X-Api-Key";
    public string? Key { get; set; }
}
