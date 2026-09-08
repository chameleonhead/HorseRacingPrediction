using EventFlow.Commands;
using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Application.Commands.Horses;

public sealed class UpdateHorseProfileCommand : Command<HorseAggregate, HorseId>
{
    public UpdateHorseProfileCommand(HorseId aggregateId, string? registeredName = null,
        string? normalizedName = null, string? sexCode = null, DateOnly? birthDate = null,
        string? ownerName = null, string? breederName = null, string? sireName = null, string? damName = null)
        : base(aggregateId)
    {
        RegisteredName = registeredName;
        NormalizedName = normalizedName;
        SexCode = sexCode;
        BirthDate = birthDate;
        OwnerName = ownerName;
        BreederName = breederName; SireName = sireName; DamName = damName;
    }

    public string? RegisteredName { get; }
    public string? NormalizedName { get; }
    public string? SexCode { get; }
    public DateOnly? BirthDate { get; }
    public string? OwnerName { get; }
    public string? BreederName { get; }
    public string? SireName { get; }
    public string? DamName { get; }
}
