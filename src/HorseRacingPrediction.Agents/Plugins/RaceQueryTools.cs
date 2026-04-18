using System.ComponentModel;
using System.Text;
using System.Text.Json;
using EventFlow.Queries;
using EventFlow.ReadStores.InMemory;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// EventFlow の <see cref="IQueryProcessor"/> を使って既存の ReadModel を照会する
/// Microsoft Agent Framework プラグイン（読み取り系）。
/// <see cref="GetAITools"/> で <see cref="AITool"/> 一覧を取得し、
/// <see cref="Microsoft.Agents.AI.ChatClientAgent"/> に渡すことで利用可能になる。
/// </summary>
public sealed class RaceQueryTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IQueryProcessor _queryProcessor;

    public RaceQueryTools(IQueryProcessor queryProcessor)
    {
        _queryProcessor = queryProcessor;
    }

    /// <summary>
    /// 指定したレース ID の予測コンテキスト（出馬表・天候・馬場状態）を取得する。
    /// </summary>
    [Description("指定したレース ID の予測コンテキスト（出馬表・天候・馬場状態）を Markdown 形式で取得します。")]
    public async Task<string> GetRacePredictionContext(
        [Description("レース ID")] string raceId,
        CancellationToken cancellationToken = default)
    {
        var query = new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId);
        var model = await _queryProcessor.ProcessAsync(query, cancellationToken);

        if (model is null || string.IsNullOrEmpty(model.RaceId))
            return $"レース ID '{raceId}' は見つかりませんでした。";

        return FormatRacePredictionContext(model);
    }

    /// <summary>
    /// 指定した馬 ID のプロフィール情報を取得する。
    /// </summary>
    [Description("指定した馬 ID のプロフィール情報（名前・性別・生年月日・エイリアス）を取得します。")]
    public async Task<string> GetHorseProfile(
        [Description("馬 ID")] string horseId,
        CancellationToken cancellationToken = default)
    {
        var query = new ReadModelByIdQuery<HorseReadModel>(horseId);
        var model = await _queryProcessor.ProcessAsync(query, cancellationToken);

        if (model is null || string.IsNullOrEmpty(model.HorseId))
            return $"馬 ID '{horseId}' は見つかりませんでした。";

        var sb = new StringBuilder();
        sb.AppendLine($"## 馬プロフィール: {model.RegisteredName}");
        sb.AppendLine($"- 馬ID: {model.HorseId}");
        sb.AppendLine($"- 登録名: {model.RegisteredName}");
        sb.AppendLine($"- 正規化名: {model.NormalizedName}");
        sb.AppendLine($"- 性別コード: {model.SexCode ?? "不明"}");
        sb.AppendLine($"- 生年月日: {model.BirthDate?.ToString("yyyy-MM-dd") ?? "不明"}");
        if (model.Aliases.Count > 0)
        {
            sb.AppendLine("- エイリアス:");
            foreach (var alias in model.Aliases)
                sb.AppendLine($"  - [{alias.AliasType}] {alias.AliasValue} (出典: {alias.SourceName})");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 指定した騎手 ID のプロフィール情報を取得する。
    /// </summary>
    [Description("指定した騎手 ID のプロフィール情報（名前・所属・エイリアス）を取得します。")]
    public async Task<string> GetJockeyProfile(
        [Description("騎手 ID")] string jockeyId,
        CancellationToken cancellationToken = default)
    {
        var query = new ReadModelByIdQuery<JockeyReadModel>(jockeyId);
        var model = await _queryProcessor.ProcessAsync(query, cancellationToken);

        if (model is null || string.IsNullOrEmpty(model.JockeyId))
            return $"騎手 ID '{jockeyId}' は見つかりませんでした。";

        var sb = new StringBuilder();
        sb.AppendLine($"## 騎手プロフィール: {model.DisplayName}");
        sb.AppendLine($"- 騎手ID: {model.JockeyId}");
        sb.AppendLine($"- 表示名: {model.DisplayName}");
        sb.AppendLine($"- 正規化名: {model.NormalizedName}");
        sb.AppendLine($"- 所属コード: {model.AffiliationCode ?? "不明"}");
        if (model.Aliases.Count > 0)
        {
            sb.AppendLine("- エイリアス:");
            foreach (var alias in model.Aliases)
                sb.AppendLine($"  - [{alias.AliasType}] {alias.AliasValue} (出典: {alias.SourceName})");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 指定した対象（馬・騎手・レースなど）に紐付くメモ一覧を取得する。
    /// </summary>
    [Description("指定した対象種別と対象IDに紐付くメモ一覧を Markdown 形式で取得します。" +
                 "subjectType には 'Horse', 'Jockey', 'Race' などを指定してください。")]
    public async Task<string> GetMemosBySubject(
        [Description("メモの対象種別（例: Horse, Jockey, Race）")] string subjectType,
        [Description("対象の ID")] string subjectId,
        CancellationToken cancellationToken = default)
    {
        var key = MemoBySubjectLocator.MakeKey(
            Enum.Parse<HorseRacingPrediction.Domain.Memos.MemoSubjectType>(subjectType, ignoreCase: true),
            subjectId);
        var query = new ReadModelByIdQuery<MemoBySubjectReadModel>(key);
        var model = await _queryProcessor.ProcessAsync(query, cancellationToken);

        if (model is null || model.Memos.Count == 0)
            return $"{subjectType}:{subjectId} に紐付くメモはありません。";

        var sb = new StringBuilder();
        sb.AppendLine($"## メモ一覧 ({subjectType}: {subjectId})");
        foreach (var memo in model.Memos)
        {
            sb.AppendLine($"### [{memo.MemoType}] {memo.CreatedAt:yyyy-MM-dd}");
            sb.AppendLine(memo.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ //
    // AI tools factory
    // ------------------------------------------------------------------ //

    /// <summary>
    /// このプラグインのメソッドを <see cref="AITool"/> 一覧として返す。
    /// </summary>
    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(GetRacePredictionContext),
        AIFunctionFactory.Create(GetHorseProfile),
        AIFunctionFactory.Create(GetJockeyProfile),
        AIFunctionFactory.Create(GetMemosBySubject)
    ];

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private static string FormatRacePredictionContext(RacePredictionContextReadModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## レース予測コンテキスト: {model.RaceName ?? model.RaceId}");
        sb.AppendLine($"- レースID: {model.RaceId}");
        sb.AppendLine($"- 開催日: {model.RaceDate?.ToString("yyyy-MM-dd") ?? "不明"}");
        sb.AppendLine($"- 競馬場コード: {model.RacecourseCode ?? "不明"}");
        sb.AppendLine($"- レース番号: {model.RaceNumber?.ToString() ?? "不明"}");
        sb.AppendLine($"- グレード: {model.GradeCode ?? "不明"}");
        sb.AppendLine($"- 馬場種別: {model.SurfaceCode ?? "不明"}");
        sb.AppendLine($"- 距離(m): {model.DistanceMeters?.ToString() ?? "不明"}");
        sb.AppendLine($"- 方向: {model.DirectionCode ?? "不明"}");
        sb.AppendLine($"- ステータス: {model.Status}");
        sb.AppendLine();

        if (model.LatestWeather is { } weather)
        {
            sb.AppendLine("### 最新天気情報");
            sb.AppendLine($"- 天気: {weather.WeatherText ?? weather.WeatherCode}");
            sb.AppendLine($"- 気温: {weather.TemperatureCelsius?.ToString("F1") ?? "不明"}℃");
            sb.AppendLine($"- 湿度: {weather.HumidityPercent?.ToString("F1") ?? "不明"}%");
            sb.AppendLine();
        }

        if (model.LatestTrackCondition is { } track)
        {
            sb.AppendLine("### 最新馬場状態");
            sb.AppendLine($"- 芝: {track.TurfConditionCode ?? "不明"}");
            sb.AppendLine($"- ダート: {track.DirtConditionCode ?? "不明"}");
            sb.AppendLine($"- 馬場説明: {track.GoingDescriptionText ?? "不明"}");
            sb.AppendLine();
        }

        if (model.Entries.Count > 0)
        {
            sb.AppendLine("### 出走馬一覧");
            sb.AppendLine("| 馬番 | 枠番 | 馬ID | 騎手ID | 調教師ID | 斤量 | 性齢 | 申告体重 |");
            sb.AppendLine("|------|------|------|--------|----------|------|------|----------|");
            foreach (var e in model.Entries.OrderBy(x => x.HorseNumber))
            {
                sb.AppendLine(
                    $"| {e.HorseNumber} | {e.GateNumber?.ToString() ?? "-"} | {e.HorseId} " +
                    $"| {e.JockeyId ?? "-"} | {e.TrainerId ?? "-"} " +
                    $"| {e.AssignedWeight?.ToString("F1") ?? "-"} " +
                    $"| {e.SexCode ?? "-"}{e.Age?.ToString() ?? "-"} " +
                    $"| {e.DeclaredWeight?.ToString("F1") ?? "-"} |");
            }
        }

        return sb.ToString();
    }
}
