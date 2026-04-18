using EventFlow.Commands;

namespace HorseRacingPrediction.Domain.Predictions;

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
        aggregate.Create(
            command.RaceId,
            command.PredictorType,
            command.PredictorId,
            command.ConfidenceScore,
            command.SummaryComment);

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
        aggregate.AddMark(
            command.EntryId,
            command.MarkCode,
            command.PredictedRank,
            command.Score,
            command.Comment);

        return Task.CompletedTask;
    }
}
