using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Horses;

public sealed class HorseProfileUpdated : AggregateEvent<HorseAggregate, HorseId>
{
    public HorseProfileUpdated(string? registeredName = null, string? normalizedName = null,
        string? sexCode = null, DateOnly? birthDate = null, string? ownerName = null,
        string? breederName = null, string? sireName = null, string? damName = null,
        string? damsireName = null, string? coatColor = null)
    {
        RegisteredName = registeredName;
        NormalizedName = normalizedName;
        SexCode = sexCode;
        BirthDate = birthDate;
        OwnerName = ownerName;
        BreederName = breederName; SireName = sireName; DamName = damName;
        DamsireName = damsireName; CoatColor = coatColor;
    }

    public string? RegisteredName { get; }
    public string? NormalizedName { get; }
    public string? SexCode { get; }
    public DateOnly? BirthDate { get; }
    public string? OwnerName { get; }
    public string? BreederName { get; }
    public string? SireName { get; }
    public string? DamName { get; }
    public string? DamsireName { get; }
    public string? CoatColor { get; }
}
