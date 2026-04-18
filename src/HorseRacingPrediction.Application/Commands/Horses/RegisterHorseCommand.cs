using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class RegisterHorseCommand : Command<HorseAggregate, HorseId>
{
    public RegisterHorseCommand(HorseId aggregateId, string registeredName, string normalizedName,
        string? sexCode = null, DateOnly? birthDate = null)
        : base(aggregateId)
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
