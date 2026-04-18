using EventFlow.Commands;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Application.Commands.Predictions;

public sealed class CreatePredictionTicketCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public CreatePredictionTicketCommand(
        PredictionTicketId aggregateId,
        string raceId,
        string predictorType,
        string predictorId,
        decimal confidenceScore,
        string? summaryComment)
        : base(aggregateId)
    {
        RaceId = raceId;
        PredictorType = predictorType;
        PredictorId = predictorId;
        ConfidenceScore = confidenceScore;
        SummaryComment = summaryComment;
    }

    public string RaceId { get; }
    public string PredictorType { get; }
    public string PredictorId { get; }
    public decimal ConfidenceScore { get; }
    public string? SummaryComment { get; }
}

public sealed class CreatePredictionTicketCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, CreatePredictionTicketCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, CreatePredictionTicketCommand command, CancellationToken cancellationToken)
    {
        aggregate.Create(command.RaceId, command.PredictorType, command.PredictorId,
            command.ConfidenceScore, command.SummaryComment);
        return Task.CompletedTask;
    }
}

public sealed class AddPredictionMarkCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public AddPredictionMarkCommand(
        PredictionTicketId aggregateId,
        string entryId,
        string markCode,
        int predictedRank,
        decimal score,
        string? comment)
        : base(aggregateId)
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

public sealed class AddPredictionMarkCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, AddPredictionMarkCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, AddPredictionMarkCommand command, CancellationToken cancellationToken)
    {
        aggregate.AddMark(command.EntryId, command.MarkCode, command.PredictedRank, command.Score, command.Comment);
        return Task.CompletedTask;
    }
}

public sealed class AddBettingSuggestionCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public AddBettingSuggestionCommand(PredictionTicketId aggregateId,
        string betTypeCode, string selectionExpression,
        decimal? stakeAmount = null, decimal? expectedValue = null)
        : base(aggregateId)
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

public sealed class AddBettingSuggestionCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, AddBettingSuggestionCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, AddBettingSuggestionCommand command, CancellationToken cancellationToken)
    {
        aggregate.AddBettingSuggestion(command.BetTypeCode, command.SelectionExpression,
            command.StakeAmount, command.ExpectedValue);
        return Task.CompletedTask;
    }
}

public sealed class AddPredictionRationaleCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public AddPredictionRationaleCommand(PredictionTicketId aggregateId,
        string subjectType, string subjectId, string signalType,
        string? signalValue = null, string? explanationText = null)
        : base(aggregateId)
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

public sealed class AddPredictionRationaleCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, AddPredictionRationaleCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, AddPredictionRationaleCommand command, CancellationToken cancellationToken)
    {
        aggregate.AddRationale(command.SubjectType, command.SubjectId, command.SignalType,
            command.SignalValue, command.ExplanationText);
        return Task.CompletedTask;
    }
}

public sealed class FinalizePredictionTicketCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public FinalizePredictionTicketCommand(PredictionTicketId aggregateId) : base(aggregateId) { }
}

public sealed class FinalizePredictionTicketCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, FinalizePredictionTicketCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, FinalizePredictionTicketCommand command, CancellationToken cancellationToken)
    {
        aggregate.FinalizeTicket();
        return Task.CompletedTask;
    }
}

public sealed class WithdrawPredictionTicketCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public WithdrawPredictionTicketCommand(PredictionTicketId aggregateId, string? reason = null)
        : base(aggregateId)
    {
        Reason = reason;
    }

    public string? Reason { get; }
}

public sealed class WithdrawPredictionTicketCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, WithdrawPredictionTicketCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, WithdrawPredictionTicketCommand command, CancellationToken cancellationToken)
    {
        aggregate.Withdraw(command.Reason);
        return Task.CompletedTask;
    }
}

public sealed class EvaluatePredictionTicketCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public EvaluatePredictionTicketCommand(PredictionTicketId aggregateId,
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

public sealed class EvaluatePredictionTicketCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, EvaluatePredictionTicketCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, EvaluatePredictionTicketCommand command, CancellationToken cancellationToken)
    {
        aggregate.Evaluate(command.RaceId, command.EvaluatedAt, command.EvaluationRevision,
            command.HitTypeCodes, command.ScoreSummary, command.ReturnAmount, command.Roi);
        return Task.CompletedTask;
    }
}

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

public sealed class RecalculatePredictionEvaluationCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, RecalculatePredictionEvaluationCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, RecalculatePredictionEvaluationCommand command, CancellationToken cancellationToken)
    {
        aggregate.RecalculateEvaluation(command.RaceId, command.EvaluatedAt, command.EvaluationRevision,
            command.HitTypeCodes, command.ScoreSummary, command.ReturnAmount, command.Roi);
        return Task.CompletedTask;
    }
}

public sealed class CorrectPredictionMetadataCommand : Command<PredictionTicketAggregate, PredictionTicketId>
{
    public CorrectPredictionMetadataCommand(PredictionTicketId aggregateId,
        decimal? confidenceScore = null, string? summaryComment = null, string? reason = null)
        : base(aggregateId)
    {
        ConfidenceScore = confidenceScore;
        SummaryComment = summaryComment;
        Reason = reason;
    }

    public decimal? ConfidenceScore { get; }
    public string? SummaryComment { get; }
    public string? Reason { get; }
}

public sealed class CorrectPredictionMetadataCommandHandler : CommandHandler<PredictionTicketAggregate, PredictionTicketId, CorrectPredictionMetadataCommand>
{
    public override Task ExecuteAsync(PredictionTicketAggregate aggregate, CorrectPredictionMetadataCommand command, CancellationToken cancellationToken)
    {
        aggregate.CorrectMetadata(command.ConfidenceScore, command.SummaryComment, command.Reason);
        return Task.CompletedTask;
    }
}
