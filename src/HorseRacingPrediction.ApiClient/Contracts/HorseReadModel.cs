namespace HorseRacingPrediction.ApiClient.Contracts;

public sealed class HorseReadModel
{
    public string HorseId { get; set; } = string.Empty;
    public string RegisteredName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? SexCode { get; set; }
    public DateOnly? BirthDate { get; set; }
    public List<HorseAliasEntry> Aliases { get; set; } = [];
}