using System.ComponentModel;
using System.Text;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// <see cref="IRaceQueryService"/> を使って既存の ReadModel を照会する
/// Microsoft Agent Framework プラグイン（読み取り系）。
/// <see cref="GetAITools"/> で <see cref="AITool"/> 一覧を取得し、
/// <see cref="Microsoft.Agents.AI.ChatClientAgent"/> に渡すことで利用可能になる。
/// </summary>
public sealed class RaceQueryTools
{
    private readonly IRaceQueryService _queryService;

    public RaceQueryTools(IRaceQueryService queryService)
    {
        _queryService = queryService;
    }

    /// <summary>
    /// 指定したレース ID の予測コンテキスト（出馬表・天候・馬場状態）を取得する。
    /// </summary>
    [Description("指定したレース ID の予測コンテキスト（出馬表・天候・馬場状態）を Markdown 形式で取得します。")]
    public async Task<string> GetRacePredictionContext(
        [Description("レース ID")] string raceId,
        CancellationToken cancellationToken = default)
    {
        var model = await _queryService.GetRacePredictionContextAsync(raceId, cancellationToken);

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
        var model = await _queryService.GetHorseAsync(horseId, cancellationToken);

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
        var model = await _queryService.GetJockeyAsync(jockeyId, cancellationToken);

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
        var model = await _queryService.GetMemosBySubjectAsync(subjectType, subjectId, cancellationToken);

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
        AIFunctionFactory.Create(GetMemosBySubject),
        AIFunctionFactory.Create(GetHorseRaceStats),
        AIFunctionFactory.Create(GetJockeyRaceStats),
        AIFunctionFactory.Create(GetRaceFieldAnalysis)
    ];

    // ------------------------------------------------------------------ //
    // Group B: 馬パフォーマンス統計ツール
    // ------------------------------------------------------------------ //

    /// <summary>
    /// 指定した馬の過去レース履歴から予測パラメーター（勝率・適性スコア・上がりタイム等）を取得する。
    /// </summary>
    [Description("指定した馬 ID の過去レース統計（勝率・複勝率・直近平均着順・距離/馬場/競馬場適性スコア・上がり3Fタイム・体重安定度・コーナー順位）を Markdown 形式で取得します。")]
    public async Task<string> GetHorseRaceStats(
        [Description("馬 ID")] string horseId,
        [Description("現在のレースの馬場種別コード（例: 芝/ダート）")] string? surfaceCode = null,
        [Description("現在のレースの距離（メートル）")] int? distanceMeters = null,
        [Description("現在のレースの競馬場コード")] string? racecourseCode = null,
        [Description("現在のレースの方向コード（例: 左/右）")] string? directionCode = null,
        [Description("現在のレース開催日（yyyy-MM-dd）")] string? currentRaceDate = null,
        CancellationToken cancellationToken = default)
    {
        var model = await _queryService.GetHorseRaceHistoryAsync(horseId, cancellationToken);

        if (model is null || string.IsNullOrEmpty(model.HorseId))
            return $"馬 ID '{horseId}' の出走履歴はありません。";

        var sb = new StringBuilder();
        sb.AppendLine($"## 馬パフォーマンス統計: {horseId}");
        sb.AppendLine($"- 総出走数: {model.TotalRaceCount}");
        sb.AppendLine($"- 勝率: {model.WinRate:P1}");
        sb.AppendLine($"- 複勝率: {model.PlaceRate:P1}");
        sb.AppendLine($"- 直近5走平均着順: {model.RecentAvgFinishPosition:F1}");
        sb.AppendLine($"- 平均上がり3Fタイム: {(model.AvgLastThreeFurlongTime > 0 ? $"{model.AvgLastThreeFurlongTime:F1}秒" : "不明")}");
        sb.AppendLine($"- 平均賞金: {(model.AvgPrizeMoney > 0 ? $"{model.AvgPrizeMoney:N0}円" : "不明")}");
        sb.AppendLine($"- 体重安定度スコア: {model.WeightStabilityScore:F1}/10");
        sb.AppendLine($"- 平均最終コーナー順位: {(model.GetAvgCornerPosition() > 0 ? $"{model.GetAvgCornerPosition():F1}" : "不明")}");

        if (surfaceCode != null)
            sb.AppendLine($"- {surfaceCode}馬場勝率: {model.GetSurfaceWinRate(surfaceCode):P1}");

        if (distanceMeters.HasValue)
            sb.AppendLine($"- 距離{distanceMeters}m適性スコア: {model.GetDistanceSuitabilityScore(distanceMeters.Value):F0}/100");

        if (racecourseCode != null)
            sb.AppendLine($"- {racecourseCode}競馬場適性スコア: {model.GetRacecourseSuitabilityScore(racecourseCode):F0}/100");

        if (directionCode != null)
            sb.AppendLine($"- {directionCode}回り適性スコア: {model.GetDirectionSuitabilityScore(directionCode):F0}/100");

        if (currentRaceDate != null && DateOnly.TryParse(currentRaceDate, out var raceDate))
        {
            var days = model.GetDaysFromLastRace(raceDate);
            sb.AppendLine($"- 前走からの間隔: {(days < 999 ? $"{days}日" : "初出走")}");
        }

        sb.AppendLine($"- 直近騎手ID: {model.LatestJockeyId ?? "不明"}");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ //
    // Group C: 騎手統計ツール
    // ------------------------------------------------------------------ //

    /// <summary>
    /// 指定した騎手の過去レース統計と馬とのコンビ実績を取得する。
    /// </summary>
    [Description("指定した騎手 ID の過去レース統計（勝率・複勝率・直近成績・馬場/距離別勝率・指定馬とのコンビ成績）を Markdown 形式で取得します。")]
    public async Task<string> GetJockeyRaceStats(
        [Description("騎手 ID")] string jockeyId,
        [Description("コンビ成績を調べる馬 ID")] string? horseId = null,
        [Description("現在のレースの馬場種別コード")] string? surfaceCode = null,
        [Description("現在のレースの距離（メートル）")] int? distanceMeters = null,
        CancellationToken cancellationToken = default)
    {
        var model = await _queryService.GetJockeyRaceHistoryAsync(jockeyId, cancellationToken);

        if (model is null || string.IsNullOrEmpty(model.JockeyId))
            return $"騎手 ID '{jockeyId}' の出走履歴はありません。";

        var sb = new StringBuilder();
        sb.AppendLine($"## 騎手統計: {jockeyId}");
        sb.AppendLine($"- 総出走数: {model.TotalRaceCount}");
        sb.AppendLine($"- 通算勝率: {model.WinRate:P1}");
        sb.AppendLine($"- 通算複勝率: {model.PlaceRate:P1}");
        sb.AppendLine($"- 直近20走勝率: {model.RecentWinRate:P1}");
        sb.AppendLine($"- 直近20走複勝率: {model.RecentPlaceRate:P1}");

        if (surfaceCode != null)
            sb.AppendLine($"- {surfaceCode}馬場勝率: {model.GetSurfaceWinRate(surfaceCode):P1}");

        if (distanceMeters.HasValue)
            sb.AppendLine($"- 距離{distanceMeters}m付近勝率: {model.GetDistanceWinRate(distanceMeters.Value):P1}");

        if (horseId != null)
        {
            sb.AppendLine($"- {horseId}とのコンビ出走数: {model.GetHorseComboCount(horseId)}");
            sb.AppendLine($"- {horseId}とのコンビ勝率: {model.GetHorseComboWinRate(horseId):P1}");
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------ //
    // Group D: レース展開分析ツール
    // ------------------------------------------------------------------ //

    /// <summary>
    /// 指定したレースの出走馬の脚質分布からレース展開を分析する（Group D パラメーター）。
    /// </summary>
    [Description("指定したレース ID の出走馬の脚質分布から展開予測（逃げ馬頭数・先行馬頭数・予想ペースタイプ・出走頭数効果）を Markdown 形式で取得します。")]
    public async Task<string> GetRaceFieldAnalysis(
        [Description("レース ID")] string raceId,
        CancellationToken cancellationToken = default)
    {
        var model = await _queryService.GetRacePredictionContextAsync(raceId, cancellationToken);

        if (model is null || string.IsNullOrEmpty(model.RaceId))
            return $"レース ID '{raceId}' は見つかりませんでした。";

        var entries = model.Entries;
        var fieldSize = entries.Count;

        var leaderCount = entries.Count(e => e.RunningStyleCode == "逃");
        var frontRunnerCount = entries.Count(e => e.RunningStyleCode == "先");
        var stalkerCount = entries.Count(e => e.RunningStyleCode == "差");
        var closerCount = entries.Count(e => e.RunningStyleCode == "追");

        // ペースタイプ: 逃げ馬が多い → ハイペース、少ない → スローペース
        var paceType = leaderCount >= 3 ? "HiPace" :
                       leaderCount == 2 ? "MidPace" : "SlowPace";

        // 出走頭数効果スコア（頭数が多いほど混雑リスク高: 0=少頭数, 100=多頭数）
        var fieldSizeEffect = Math.Min(100, (fieldSize - 6) * 100.0 / 12.0);

        var sb = new StringBuilder();
        sb.AppendLine($"## レース展開分析: {model.RaceName ?? raceId}");
        sb.AppendLine($"- レースID: {raceId}");
        sb.AppendLine($"- 出走頭数: {fieldSize}頭");
        sb.AppendLine($"- 逃げ馬頭数（FieldLeaderCount）: {leaderCount}");
        sb.AppendLine($"- 先行馬頭数（FieldFrontRunnerCount）: {frontRunnerCount}");
        sb.AppendLine($"- 差し馬頭数: {stalkerCount}");
        sb.AppendLine($"- 追込馬頭数: {closerCount}");
        sb.AppendLine($"- 予想ペースタイプ（FavoredPaceType）: {paceType}");
        sb.AppendLine($"- 出走頭数効果スコア（FieldSizeEffect）: {fieldSizeEffect:F0}/100");
        sb.AppendLine();
        sb.AppendLine("### 各馬の予想道中ポジション（ExpectedRacePosition）");
        sb.AppendLine("| 馬番 | 馬ID | 脚質 | 予想ポジション |");
        sb.AppendLine("|------|------|------|--------------|");
        foreach (var e in entries.OrderBy(x => x.HorseNumber))
        {
            var pos = EstimateRacePosition(e.RunningStyleCode, leaderCount, frontRunnerCount);
            sb.AppendLine($"| {e.HorseNumber} | {e.HorseId} | {e.RunningStyleCode ?? "-"} | {pos} |");
        }

        return sb.ToString();
    }

    private static string EstimateRacePosition(string? runningStyleCode, int leaderCount, int frontRunnerCount)
    {
        return runningStyleCode switch
        {
            "逃" => "1〜2番手",
            "先" => leaderCount == 0 ? "1〜3番手" : $"{leaderCount + 1}〜{leaderCount + frontRunnerCount + 1}番手",
            "差" => "中団",
            "追" => "後方",
            _ => "不明"
        };
    }

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
            sb.AppendLine("| 馬番 | 枠番 | 馬ID | 騎手ID | 調教師ID | 斤量 | 性齢 | 申告体重 | 脚質 |");
            sb.AppendLine("|------|------|------|--------|----------|------|------|----------|------|");
            foreach (var e in model.Entries.OrderBy(x => x.HorseNumber))
            {
                sb.AppendLine(
                    $"| {e.HorseNumber} | {e.GateNumber?.ToString() ?? "-"} | {e.HorseId} " +
                    $"| {e.JockeyId ?? "-"} | {e.TrainerId ?? "-"} " +
                    $"| {e.AssignedWeight?.ToString("F1") ?? "-"} " +
                    $"| {e.SexCode ?? "-"}{e.Age?.ToString() ?? "-"} " +
                    $"| {e.DeclaredWeight?.ToString("F1") ?? "-"} " +
                    $"| {e.RunningStyleCode ?? "-"} |");
            }
        }

        return sb.ToString();
    }
}
