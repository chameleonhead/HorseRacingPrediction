using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Predictions;

public sealed class PredictionTicketCreated : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public PredictionTicketCreated(
        string raceId,
        string predictorType,
        string predictorId,
        decimal confidenceScore,
        string? summaryComment)
    {
        RaceId = raceId;
        PredictorType = predictorType;
        PredictorId = predictorId;
        ConfidenceScore = confidenceScore;
        SummaryComment = summaryComment;
        PredictedAt = DateTimeOffset.UtcNow;
    }

    public string RaceId { get; }
    public string PredictorType { get; }
    public string PredictorId { get; }
    public decimal ConfidenceScore { get; }
    public string? SummaryComment { get; }
    public DateTimeOffset PredictedAt { get; }
}

public sealed class PredictionMarkAdded : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public PredictionMarkAdded(string entryId, string markCode, int predictedRank, decimal score, string? comment)
    {
        EntryId = entryId;
        MarkCode = markCode;
        PredictedRank = predictedRank;
        Score = score;
        Comment = comment;
    }

    public string EntryId { get; }
    public string MarkCode { get; }
    public int PredictedRank { get; }
    public decimal Score { get; }
    public string? Comment { get; }
}

public sealed class BettingSuggestionAdded : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public BettingSuggestionAdded(string betTypeCode, string selectionExpression,
        decimal? stakeAmount = null, decimal? expectedValue = null)
    {
        BetTypeCode = betTypeCode;
        SelectionExpression = selectionExpression;
        StakeAmount = stakeAmount;
        ExpectedValue = expectedValue;
    }

    public string BetTypeCode { get; }
    public string SelectionExpression { get; }
    public decimal? StakeAmount { get; }
    public decimal? ExpectedValue { get; }
}

public sealed class PredictionRationaleAdded : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public PredictionRationaleAdded(string subjectType, string subjectId, string signalType,
        string? signalValue = null, string? explanationText = null)
    {
        SubjectType = subjectType;
        SubjectId = subjectId;
        SignalType = signalType;
        SignalValue = signalValue;
        ExplanationText = explanationText;
    }

    public string SubjectType { get; }
    public string SubjectId { get; }
    public string SignalType { get; }
    public string? SignalValue { get; }
    public string? ExplanationText { get; }
}

public sealed class PredictionTicketFinalized : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
}

public sealed class PredictionTicketWithdrawn : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public PredictionTicketWithdrawn(string? reason = null)
    {
        Reason = reason;
    }

    public string? Reason { get; }
}

public sealed class PredictionTicketEvaluated : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public PredictionTicketEvaluated(string raceId, DateTimeOffset evaluatedAt,
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

public sealed class PredictionMetadataCorrected : AggregateEvent<PredictionTicketAggregate, PredictionTicketId>
{
    public PredictionMetadataCorrected(decimal? confidenceScore = null, string? summaryComment = null,
        string? reason = null)
    {
        ConfidenceScore = confidenceScore;
        SummaryComment = summaryComment;
        Reason = reason;
    }

    public decimal? ConfidenceScore { get; }
    public string? SummaryComment { get; }
    public string? Reason { get; }
}
