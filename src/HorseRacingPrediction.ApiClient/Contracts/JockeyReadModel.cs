namespace HorseRacingPrediction.ApiClient.Contracts;

public sealed class JockeyReadModel
{
    public string JockeyId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? AffiliationCode { get; set; }
    public List<JockeyAliasEntry> Aliases { get; set; } = [];
}