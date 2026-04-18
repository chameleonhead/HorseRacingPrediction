using System.ComponentModel;
using EventFlow;
using EventFlow.Commands;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Domain.Predictions;
using Microsoft.SemanticKernel;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// EventFlow の <see cref="ICommandBus"/> を使って予測票を作成・更新する
/// Semantic Kernel プラグイン（書き込み系）。
/// 各エージェントの Kernel に <c>AddFromObject</c> で登録することで、
/// <c>[KernelFunction]</c> メソッドがツールとして利用可能になる。
/// </summary>
public sealed class PredictionWriteTools
{
    private readonly ICommandBus _commandBus;

    public PredictionWriteTools(ICommandBus commandBus)
    {
        _commandBus = commandBus;
    }

    /// <summary>
    /// 新しい予測票を作成し、その ID を返す。
    /// </summary>
    [KernelFunction]
    [Description("新しい予測票を作成します。作成された予測票の ID を返します。")]
    public async Task<string> CreatePredictionTicket(
        [Description("対象レース ID")] string raceId,
        [Description("予測者種別（例: AI, Human）")] string predictorType,
        [Description("予測者 ID（エージェント名やユーザー ID）")] string predictorId,
        [Description("信頼度スコア（0.0〜1.0）")] decimal confidenceScore,
        [Description("予測のサマリーコメント（省略可）")] string? summaryComment = null,
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

    /// <summary>
    /// 予測票に出走馬の予測印を追加する。
    /// </summary>
    [KernelFunction]
    [Description("予測票に出走馬の予測印（◎○▲△）を追加します。")]
    public async Task<string> AddPredictionMark(
        [Description("予測票 ID")] string predictionTicketId,
        [Description("出走エントリー ID")] string entryId,
        [Description("予測印コード（例: ◎ ○ ▲ △）")] string markCode,
        [Description("予測着順（1〜）")] int predictedRank,
        [Description("スコア（0.0〜1.0）")] decimal score,
        [Description("コメント（省略可）")] string? comment = null,
        CancellationToken cancellationToken = default)
    {
        var ticketId = new PredictionTicketId(predictionTicketId);
        var command = new AddPredictionMarkCommand(ticketId, entryId, markCode, predictedRank, score, comment);

        var result = await _commandBus.PublishAsync(command, cancellationToken);
        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"予測印の追加に失敗しました: ticketId={predictionTicketId}, entryId={entryId}");

        return $"エントリー {entryId} に予測印 {markCode}（予測{predictedRank}着）を追加しました。";
    }

    /// <summary>
    /// 予測票に根拠・シグナルを追加する。
    /// </summary>
    [KernelFunction]
    [Description("予測票に特定の馬・騎手・コースなどを対象とした予測根拠シグナルを追加します。")]
    public async Task<string> AddPredictionRationale(
        [Description("予測票 ID")] string predictionTicketId,
        [Description("対象種別（例: Horse, Jockey, Race）")] string subjectType,
        [Description("対象 ID")] string subjectId,
        [Description("シグナル種別（例: RecentForm, CourseAptitude, WeatherFit）")] string signalType,
        [Description("シグナル値（数値や文字列、省略可）")] string? signalValue = null,
        [Description("シグナルの説明テキスト（省略可）")] string? explanationText = null,
        CancellationToken cancellationToken = default)
    {
        var ticketId = new PredictionTicketId(predictionTicketId);
        var command = new AddPredictionRationaleCommand(
            ticketId, subjectType, subjectId, signalType, signalValue, explanationText);

        var result = await _commandBus.PublishAsync(command, cancellationToken);
        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"予測根拠の追加に失敗しました: ticketId={predictionTicketId}");

        return $"予測根拠 ({subjectType}:{subjectId} / {signalType}) を追加しました。";
    }

    /// <summary>
    /// 予測票を確定（ファイナライズ）する。確定後は変更不可。
    /// </summary>
    [KernelFunction]
    [Description("予測票を確定します。確定後は予測印・根拠の追加ができなくなります。")]
    public async Task<string> FinalizePredictionTicket(
        [Description("確定する予測票 ID")] string predictionTicketId,
        CancellationToken cancellationToken = default)
    {
        var ticketId = new PredictionTicketId(predictionTicketId);
        var command = new FinalizePredictionTicketCommand(ticketId);

        var result = await _commandBus.PublishAsync(command, cancellationToken);
        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"予測票の確定に失敗しました: ticketId={predictionTicketId}");

        return $"予測票 {predictionTicketId} を確定しました。";
    }
}
