using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Horses;

public sealed class HorseDataCorrected : AggregateEvent<HorseAggregate, HorseId>
{
    public HorseDataCorrected(string? registeredName = null, string? normalizedName = null,
        string? sexCode = null, DateOnly? birthDate = null, string? reason = null)
    {
        RegisteredName = registeredName;
        NormalizedName = normalizedName;
        SexCode = sexCode;
        BirthDate = birthDate;
        Reason = reason;
    }

    public string? RegisteredName { get; }
    public string? NormalizedName { get; }
    public string? SexCode { get; }
    public DateOnly? BirthDate { get; }
    public string? Reason { get; }
}
