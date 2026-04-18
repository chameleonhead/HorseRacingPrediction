using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Horses;

public sealed class HorseRegistered : AggregateEvent<HorseAggregate, HorseId>
{
    public HorseRegistered(string registeredName, string normalizedName,
        string? sexCode = null, DateOnly? birthDate = null)
    {
        RegisteredName = registeredName;
        NormalizedName = normalizedName;
        SexCode = sexCode;
        BirthDate = birthDate;
    }

    public string RegisteredName { get; }
    public string NormalizedName { get; }
    public string? SexCode { get; }
    public DateOnly? BirthDate { get; }
}
