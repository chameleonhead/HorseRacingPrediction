namespace HorseRacingPrediction.ApiClient.Contracts;

public sealed class TrainerReadModel
{
    public string TrainerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? AffiliationCode { get; set; }
    public List<TrainerAliasEntry> Aliases { get; set; } = [];
}