using HorseRacingPrediction.ApiClient;

namespace HorseRacingPrediction.JraVerifier;

internal sealed class NoOpDataCollectionWriteService : IDataCollectionWriteService
{
    public Task<string> UpsertRaceAsync(
        string raceDate,
        string racecourseCode,
        int raceNumber,
        string raceName,
        int? entryCount,
        string? gradeCode,
        string? surfaceCode,
        int? distanceMeters,
        string? directionCode,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"race:{raceDate}:{racecourseCode}:{raceNumber}");

    public Task<string> UpsertHorseAsync(
        string registeredName,
        string? normalizedName,
        string? sexCode,
        string? birthDate,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"horse:{registeredName}");

    public Task<string> UpsertJockeyAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"jockey:{displayName}");

    public Task<string> UpsertTrainerAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"trainer:{displayName}");

    public Task<string> UpsertRaceEntryAsync(
        string raceId,
        int horseNumber,
        string horseName,
        string? jockeyName,
        string? trainerName,
        int? gateNumber,
        decimal? assignedWeight,
        string? sexCode,
        int? age,
        decimal? declaredWeight,
        decimal? declaredWeightDiff,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"entry:{raceId}:{horseNumber}");

    public Task<string> DeclareRaceResultAsync(
        string raceId,
        string winningHorseName,
        string? declaredAt,
        string? winningHorseId,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"result:{raceId}");

    public Task<string> DeclareRaceEntryResultAsync(
        string raceId,
        int horseNumber,
        int? finishPosition,
        string? officialTime,
        string? marginText,
        string? lastThreeFurlongTime,
        string? abnormalResultCode,
        decimal? prizeMoney,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"entry-result:{raceId}:{horseNumber}");

    public Task<string> DeclareRacePayoutsAsync(
        string raceId,
        string? winPayoutsJson,
        string? placePayoutsJson,
        string? quinellaPayoutsJson,
        string? exactaPayoutsJson,
        string? trifectaPayoutsJson,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"payout:{raceId}");
}