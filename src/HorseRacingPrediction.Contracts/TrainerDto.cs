namespace HorseRacingPrediction.Contracts;

public sealed class TrainerDto
{
    public string TrainerId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? AffiliationCode { get; set; }
    public List<TrainerAliasEntry> Aliases { get; set; } = [];
}