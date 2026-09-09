namespace HorseRacingPrediction.Contracts;

public sealed class HorseReadModel
{
    public string HorseId { get; set; } = string.Empty;
    public string RegisteredName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? SexCode { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? OwnerName { get; set; }
    public string? BreederName { get; set; }
    public string? SireName { get; set; }
    public string? DamName { get; set; }
    public string? DamsireName { get; set; }
    public string? CoatColor { get; set; }
    public List<HorseAliasEntry> Aliases { get; set; } = [];
}
