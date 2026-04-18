using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record RaceResultViewReadModel(
    string RaceId,
    DateOnly RaceDate,
    string RacecourseCode,
    int RaceNumber,
    string RaceName,
    RaceStatus Status,
    string? WinningHorseName,
    string? WinningHorseId,
    DateTimeOffset? ResultDeclaredAt,
    string? StewardReportText,
    IReadOnlyList<EntryResultSnapshot> EntryResults,
    PayoutResultSnapshot? PayoutResult);

public sealed record EntryResultSnapshot(
    string EntryId,
    string HorseId,
    int HorseNumber,
    int? FinishPosition,
    string? OfficialTime,
    string? MarginText,
    string? LastThreeFurlongTime,
    string? AbnormalResultCode,
    decimal? PrizeMoney);

public sealed record PayoutResultSnapshot(
    DateTimeOffset DeclaredAt,
    IReadOnlyList<PayoutEntrySnapshot> WinPayouts,
    IReadOnlyList<PayoutEntrySnapshot> PlacePayouts,
    IReadOnlyList<PayoutEntrySnapshot> QuinellaPayouts,
    IReadOnlyList<PayoutEntrySnapshot> ExactaPayouts,
    IReadOnlyList<PayoutEntrySnapshot> TrifectaPayouts);

public sealed record PayoutEntrySnapshot(string Combination, decimal Amount);
