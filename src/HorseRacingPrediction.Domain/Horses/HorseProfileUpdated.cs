using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Horses;

public sealed class HorseProfileUpdated : AggregateEvent<HorseAggregate, HorseId>
{
    public HorseProfileUpdated(string? registeredName = null, string? normalizedName = null,
        string? sexCode = null, DateOnly? birthDate = null)
    {
        RegisteredName = registeredName;
        NormalizedName = normalizedName;
        SexCode = sexCode;
        BirthDate = birthDate;
    }

    public string? RegisteredName { get; }
    public string? NormalizedName { get; }
    public string? SexCode { get; }
    public DateOnly? BirthDate { get; }
}
