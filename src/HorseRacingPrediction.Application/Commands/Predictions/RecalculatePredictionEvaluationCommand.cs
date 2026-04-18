using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class RecalculatePredictionEvaluationCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public RecalculatePredictionEvaluationCommand(PredictionTicketId aggregateId,
        string raceId, DateTimeOffset evaluatedAt, int evaluationRevision,
        IReadOnlyList<string> hitTypeCodes,
        decimal? scoreSummary = null, decimal? returnAmount = null, decimal? roi = null)
        : base(aggregateId)
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
