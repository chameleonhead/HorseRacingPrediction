using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

public sealed class PredictionComparisonTicketState
{
    public string PredictionTicketId { get; set; } = string.Empty;
    public string PredictorType { get; set; } = string.Empty;
    public string PredictorId { get; set; } = string.Empty;
    public TicketStatus Status { get; set; } = TicketStatus.Draft;
    public decimal ConfidenceScore { get; set; }
    public string? SummaryComment { get; set; }
    public DateTimeOffset PredictedAt { get; set; }
    public List<PredictionMarkSnapshot> Marks { get; set; } = [];
    public List<PredictionEvaluationSnapshot> Evaluations { get; set; } = [];
    public EvaluationStatus EvaluationStatus { get; set; } = EvaluationStatus.Ready;

    public PredictionTicketSnapshot ToSnapshot() => new(
        PredictionTicketId,
        PredictorType,
        PredictorId,
        Status,
        ConfidenceScore,
        SummaryComment,
        PredictedAt,
        Marks.ToList(),
        Evaluations.OrderByDescending(e => e.EvaluationRevision).FirstOrDefault(),
        EvaluationStatus);
}