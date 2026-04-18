using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class CorrectHorseDataCommand : Command<HorseAggregate, HorseId>
{
    public CorrectHorseDataCommand(HorseId aggregateId, string? registeredName = null,
        string? normalizedName = null, string? sexCode = null, DateOnly? birthDate = null, string? reason = null)
        : base(aggregateId)
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
