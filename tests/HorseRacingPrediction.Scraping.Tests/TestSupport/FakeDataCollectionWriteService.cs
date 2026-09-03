using HorseRacingPrediction.ApiClient;

namespace HorseRacingPrediction.Scraping.Tests.TestSupport;

/// <summary>
/// Workflow層のテスト用フェイク。呼び出された内容を記録し、決定論的な ID を返す。
/// </summary>
internal sealed class FakeDataCollectionWriteService : IDataCollectionWriteService
{
    public sealed record UpsertRaceCall(
        string RaceDate,
        string RacecourseCode,
        int RaceNumber,
        string RaceName,
        int? EntryCount);

    public sealed record UpsertRaceEntryCall(
        string RaceId,
        int HorseNumber,
        string HorseName,
        string? JockeyName,
        string? TrainerName,
        int? GateNumber,
        decimal? AssignedWeight);

    public List<UpsertRaceCall> UpsertRaceCalls { get; } = [];

    public List<UpsertRaceEntryCall> UpsertRaceEntryCalls { get; } = [];

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
    {
        UpsertRaceCalls.Add(new UpsertRaceCall(raceDate, racecourseCode, raceNumber, raceName, entryCount));
        return Task.FromResult($"race-{raceDate}-{racecourseCode}-{raceNumber}");
    }

    public Task<string> UpsertHorseAsync(
        string registeredName,
        string? normalizedName,
        string? sexCode,
        string? birthDate,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"horse-{registeredName}");

    public Task<string> UpsertJockeyAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"jockey-{displayName}");

    public Task<string> UpsertTrainerAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default)
        => Task.FromResult($"trainer-{displayName}");

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
    {
        UpsertRaceEntryCalls.Add(new UpsertRaceEntryCall(
            raceId, horseNumber, horseName, jockeyName, trainerName, gateNumber, assignedWeight));
        return Task.FromResult($"{raceId}-entry-{horseNumber}");
    }

    public Task<string> DeclareRaceResultAsync(
        string raceId,
        string winningHorseName,
        string? declaredAt,
        string? winningHorseId,
        CancellationToken cancellationToken = default)
        => Task.FromResult("declared");

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
        => Task.FromResult("declared");

    public Task<string> DeclareRacePayoutsAsync(
        string raceId,
        string? winPayoutsJson,
        string? placePayoutsJson,
        string? quinellaPayoutsJson,
        string? exactaPayoutsJson,
        string? trifectaPayoutsJson,
        CancellationToken cancellationToken = default)
        => Task.FromResult("declared");
}
