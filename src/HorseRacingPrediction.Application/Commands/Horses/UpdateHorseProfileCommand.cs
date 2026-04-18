using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class UpdateHorseProfileCommand : Command<HorseAggregate, HorseId>
{
    public UpdateHorseProfileCommand(HorseId aggregateId, string? registeredName = null,
        string? normalizedName = null, string? sexCode = null, DateOnly? birthDate = null)
        : base(aggregateId)
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
