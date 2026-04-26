using EventFlow;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Domain.Predictions;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// EventFlow の <see cref="ICommandBus"/> を使って <see cref="IPredictionWriteService"/> を実装するクラス。
/// </summary>
public sealed class EventFlowPredictionWriteService : IPredictionWriteService
{
    private readonly ICommandBus _commandBus;

    public EventFlowPredictionWriteService(ICommandBus commandBus)
    {
        _commandBus = commandBus;
    }

    public async Task<string> CreatePredictionTicketAsync(
        string raceId,
        string predictorType,
        string predictorId,
        decimal confidenceScore,
        string? summaryComment,
        CancellationToken cancellationToken = default)
    {
        var ticketId = PredictionTicketId.New;
        var command = new CreatePredictionTicketCommand(
            ticketId, raceId, predictorType, predictorId, confidenceScore, summaryComment);

        var result = await _commandBus.PublishAsync(command, cancellationToken);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"予測票の作成に失敗しました: raceId={raceId}");

        return ticketId.Value;
    }

    public async Task AddPredictionMarkAsync(
        string predictionTicketId,
        string entryId,
        string markCode,
        int predictedRank,
        decimal score,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var ticketId = new PredictionTicketId(predictionTicketId);
        var command = new AddPredictionMarkCommand(ticketId, entryId, markCode, predictedRank, score, comment);

        var result = await _commandBus.PublishAsync(command, cancellationToken);
        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"予測印の追加に失敗しました: ticketId={predictionTicketId}, entryId={entryId}");
    }

    public async Task AddPredictionRationaleAsync(
        string predictionTicketId,
        string subjectType,
        string subjectId,
        string signalType,
        string? signalValue,
        string? explanationText,
        CancellationToken cancellationToken = default)
    {
        var ticketId = new PredictionTicketId(predictionTicketId);
        var command = new AddPredictionRationaleCommand(
            ticketId, subjectType, subjectId, signalType, signalValue, explanationText);

        var result = await _commandBus.PublishAsync(command, cancellationToken);
        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"予測根拠の追加に失敗しました: ticketId={predictionTicketId}");
    }

    public async Task FinalizePredictionTicketAsync(
        string predictionTicketId,
        CancellationToken cancellationToken = default)
    {
        var ticketId = new PredictionTicketId(predictionTicketId);
        var command = new FinalizePredictionTicketCommand(ticketId);

        var result = await _commandBus.PublishAsync(command, cancellationToken);
        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"予測票の確定に失敗しました: ticketId={predictionTicketId}");
    }
}
