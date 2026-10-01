using HorseRacingPrediction.Contracts.Horses;
using HorseRacingPrediction.Contracts.Jockeys;
using HorseRacingPrediction.Contracts.Trainers;

namespace HorseRacingPrediction.Api.Tests;

/// <summary>Builds the registered-subject HTTP envelopes while keeping test call sites' input ordering explicit.</summary>
internal static class SubjectRequestFactory
{
    public static RegisterHorseRequest RegisterHorse(
        string registeredName,
        string normalizedName,
        string? sexCode,
        DateOnly? birthDate,
        string? horseId = null,
        string? ownerName = null,
        string? breederName = null,
        string? sireName = null,
        string? damName = null,
        string? damsireName = null,
        string? coatColor = null) =>
        new(new RegisterHorseInputDto(registeredName, normalizedName, sexCode, birthDate, horseId, ownerName, breederName, sireName, damName, damsireName, coatColor));

    public static RegisterJockeyRequest RegisterJockey(
        string displayName,
        string normalizedName,
        string? affiliationCode,
        string? jockeyId = null) =>
        new(new RegisterJockeyInputDto(displayName, normalizedName, affiliationCode, jockeyId));

    public static RegisterTrainerRequest RegisterTrainer(
        string displayName,
        string normalizedName,
        string? affiliationCode,
        string? trainerId = null) =>
        new(new RegisterTrainerInputDto(displayName, normalizedName, affiliationCode, trainerId));
}
