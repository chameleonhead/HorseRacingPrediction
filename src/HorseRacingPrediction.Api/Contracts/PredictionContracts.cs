using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record CreatePredictionTicketRequest(
    [property: Required, StringLength(64, MinimumLength = 1)] string RaceId,
    [property: Required, StringLength(32, MinimumLength = 1)] string PredictorType,
    [property: Required, StringLength(64, MinimumLength = 1)] string PredictorId,
    [property: Range(0, 1)] decimal ConfidenceScore,
    [property: StringLength(300)] string? SummaryComment);

public sealed record AddPredictionMarkRequest(
    [property: Required, StringLength(64, MinimumLength = 1)] string EntryId,
    [property: Required, StringLength(8, MinimumLength = 1)] string MarkCode,
    [property: Range(1, 40)] int PredictedRank,
    [property: Range(0, 100)] decimal Score,
    [property: StringLength(300)] string? Comment);

public sealed record PredictionMarkResponse(
    string EntryId,
    string MarkCode,
    int PredictedRank,
    decimal Score,
    string? Comment);

public sealed record PredictionTicketResponse(
    string PredictionTicketId,
    string? RaceId,
    string? PredictorType,
    string? PredictorId,
    decimal ConfidenceScore,
    string? SummaryComment,
    DateTimeOffset? PredictedAt,
    IReadOnlyCollection<PredictionMarkResponse> Marks);
