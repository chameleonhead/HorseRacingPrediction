using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// <see cref="IPredictionWriteService"/> を使って予測票を作成・更新する
/// Microsoft Agent Framework プラグイン（書き込み系）。
/// <see cref="GetAITools"/> で <see cref="AITool"/> 一覧を取得し、
/// <see cref="Microsoft.Agents.AI.ChatClientAgent"/> に渡すことで利用可能になる。
/// </summary>
public sealed class PredictionWriteTools
{
    private readonly IPredictionWriteService _service;

    public PredictionWriteTools(IPredictionWriteService service)
    {
        _service = service;
    }

    /// <summary>
    /// 新しい予測票を作成し、その ID を返す。
    /// </summary>
    [Description("新しい予測票を作成します。作成された予測票の ID を返します。")]
    public async Task<string> CreatePredictionTicket(
        [Description("対象レース ID")] string raceId,
        [Description("予測者種別（例: AI, Human）")] string predictorType,
        [Description("予測者 ID（エージェント名やユーザー ID）")] string predictorId,
        [Description("信頼度スコア（0.0〜1.0）")] decimal confidenceScore,
        [Description("予測のサマリーコメント（省略可）")] string? summaryComment = null,
        CancellationToken cancellationToken = default)
    {
        return await _service.CreatePredictionTicketAsync(
            raceId, predictorType, predictorId, confidenceScore, summaryComment, cancellationToken);
    }

    /// <summary>
    /// 予測票に出走馬の予測印を追加する。
    /// </summary>
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
        await _service.AddPredictionMarkAsync(
            predictionTicketId, entryId, markCode, predictedRank, score, comment, cancellationToken);
        return $"エントリー {entryId} に予測印 {markCode}（予測{predictedRank}着）を追加しました。";
    }

    /// <summary>
    /// 予測票に根拠・シグナルを追加する。
    /// </summary>
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
        await _service.AddPredictionRationaleAsync(
            predictionTicketId, subjectType, subjectId, signalType, signalValue, explanationText, cancellationToken);
        return $"予測根拠 ({subjectType}:{subjectId} / {signalType}) を追加しました。";
    }

    /// <summary>
    /// 予測票を確定（ファイナライズ）する。確定後は変更不可。
    /// </summary>
    [Description("予測票を確定します。確定後は予測印・根拠の追加ができなくなります。")]
    public async Task<string> FinalizePredictionTicket(
        [Description("確定する予測票 ID")] string predictionTicketId,
        CancellationToken cancellationToken = default)
    {
        await _service.FinalizePredictionTicketAsync(predictionTicketId, cancellationToken);
        return $"予測票 {predictionTicketId} を確定しました。";
    }

    /// <summary>
    /// このプラグインのメソッドを <see cref="AITool"/> 一覧として返す。
    /// </summary>
    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(CreatePredictionTicket),
        AIFunctionFactory.Create(AddPredictionMark),
        AIFunctionFactory.Create(AddPredictionRationale),
        AIFunctionFactory.Create(FinalizePredictionTicket)
    ];
}
