using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public sealed class PredictionEvaluationRecalculated : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public PredictionEvaluationRecalculated(string raceId, DateTimeOffset evaluatedAt,
        int evaluationRevision, IReadOnlyList<string> hitTypeCodes,
        decimal? scoreSummary = null, decimal? returnAmount = null, decimal? roi = null)
    {
        RaceId = raceId;
        EvaluatedAt = evaluatedAt;
        EvaluationRevision = evaluationRevision;
        HitTypeCodes = hitTypeCodes;
        ScoreSummary = scoreSummary;
        ReturnAmount = returnAmount;
        Roi = roi;
    }

    public string RaceId { get; }
    public DateTimeOffset EvaluatedAt { get; }
    public int EvaluationRevision { get; }
    public IReadOnlyList<string> HitTypeCodes { get; }
    public decimal? ScoreSummary { get; }
    public decimal? ReturnAmount { get; }
    public decimal? Roi { get; }
}
