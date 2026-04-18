using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed record PredictionComparisonViewReadModel(
    string RaceId,
    string RaceName,
    IReadOnlyList<PredictionTicketSnapshot> PredictionTickets,
    IReadOnlyList<EntryResultSnapshot> EntryResults);

public sealed record PredictionTicketSnapshot(
    string PredictionTicketId,
    string PredictorType,
    string PredictorId,
    TicketStatus Status,
    decimal ConfidenceScore,
    string? SummaryComment,
    DateTimeOffset PredictedAt,
    IReadOnlyList<PredictionMarkSnapshot> Marks,
    PredictionEvaluationSnapshot? LatestEvaluation,
    EvaluationStatus EvaluationStatus);

public sealed record PredictionMarkSnapshot(
    string EntryId,
    string MarkCode,
    int PredictedRank,
    decimal Score,
    string? Comment);

public sealed record PredictionEvaluationSnapshot(
    DateTimeOffset EvaluatedAt,
    int EvaluationRevision,
    IReadOnlyList<string> HitTypeCodes,
    decimal? ScoreSummary,
    decimal? ReturnAmount,
    decimal? Roi);

public enum EvaluationStatus
{
    Ready,
    RecalculationRequired,
    Failed
}
