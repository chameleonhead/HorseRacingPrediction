namespace HorseRacingPrediction.Agents.JraAgent;

// ──────────────────────────────────────────────────────────────────────────────
// ナビゲーション証跡
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 目的ページに到達するまでに取った操作ステップと所要時間を記録する。
/// </summary>
public sealed record JraNavigationTrace(
    IReadOnlyList<string> Steps,
    TimeSpan Elapsed);

// ──────────────────────────────────────────────────────────────────────────────
// 統一抽出結果エンベロープ
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// エージェントのあらゆる抽出操作の統一返却型。
/// 成否・ページ種別・ナビゲーション経路・抽出データを一括で保持する。
/// </summary>
public sealed record JraExtractionEnvelope(
    bool Success,
    JraPageKind PageKind,
    string SourceUrl,
    JraNavigationTrace Trace,
    object? Data,
    string? Error = null)
{
    /// <summary>抽出データを指定型にキャストして返す。型が合わない場合は null。</summary>
    public T? GetData<T>() where T : class => Data as T;

    /// <summary>
    /// 非ジェネリック結果をジェネリック結果へ変換する。
    /// Success=true かつ型不一致の場合は失敗結果に変換する。
    /// </summary>
    public JraExtractionEnvelope<T> ToTyped<T>() where T : class
    {
        if (!Success)
        {
            return JraExtractionEnvelope<T>.Failure(
                PageKind,
                SourceUrl,
                Trace,
                Error ?? "抽出処理に失敗しました。");
        }

        if (Data is T typed)
        {
            return new JraExtractionEnvelope<T>(true, PageKind, SourceUrl, Trace, typed, Error);
        }

        var actualType = Data?.GetType().Name ?? "null";
        return JraExtractionEnvelope<T>.Failure(
            PageKind,
            SourceUrl,
            Trace,
            $"抽出データ型が期待と異なります。expected={typeof(T).Name}, actual={actualType}");
    }

    /// <summary>失敗エンベロープを生成するファクトリ。</summary>
    public static JraExtractionEnvelope Failure(
        JraPageKind kind,
        string url,
        JraNavigationTrace trace,
        string error)
        => new(false, kind, url, trace, null, error);
}

/// <summary>
/// 型付けされた抽出結果。
/// データベース登録など後続処理で object キャストを不要にするための返却型。
/// </summary>
public sealed record JraExtractionEnvelope<T>(
    bool Success,
    JraPageKind PageKind,
    string SourceUrl,
    JraNavigationTrace Trace,
    T? Data,
    string? Error = null)
    where T : class
{
    /// <summary>失敗エンベロープを生成するファクトリ。</summary>
    public static JraExtractionEnvelope<T> Failure(
        JraPageKind kind,
        string url,
        JraNavigationTrace trace,
        string error)
        => new(false, kind, url, trace, null, error);
}

// ──────────────────────────────────────────────────────────────────────────────
// オッズページ結果
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>JRA オッズページから抽出したデータ。</summary>
public sealed record JraOddsResult(
    string? RaceName,
    DateOnly? RaceDate,
    string? Racecourse,
    int? RaceNumber,
    IReadOnlyList<JraWinOddsEntry> WinOdds,
    IReadOnlyList<JraPlaceOddsEntry> PlaceOdds,
    string SourceUrl);

/// <summary>単勝オッズエントリー。</summary>
public sealed record JraWinOddsEntry(
    int HorseNumber,
    string? HorseName,
    decimal? Odds,
    int? Popularity);

/// <summary>複勝オッズエントリー（最小〜最大レンジ）。</summary>
public sealed record JraPlaceOddsEntry(
    int HorseNumber,
    string? HorseName,
    decimal? OddsMin,
    decimal? OddsMax);

// ──────────────────────────────────────────────────────────────────────────────
// レース結果ページ結果
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>JRA レース結果ページから抽出したデータ。</summary>
public sealed record JraRaceResultSummary(
    string? RaceName,
    DateOnly? RaceDate,
    string? Racecourse,
    int? RaceNumber,
    IReadOnlyList<JraResultEntry> Entries,
    IReadOnlyList<JraPayoutSummary> Payouts,
    string SourceUrl);

/// <summary>着順エントリー。</summary>
public sealed record JraResultEntry(
    int? FinishPosition,
    int HorseNumber,
    string? HorseName,
    string? JockeyName,
    string? FinishTime,
    decimal? Weight);

/// <summary>払戻金エントリー。</summary>
public sealed record JraPayoutSummary(
    string BetType,
    string Combination,
    string Payout);

// ──────────────────────────────────────────────────────────────────────────────
// エンティティプロフィール（馬・騎手・調教師共通）
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 馬・騎手・調教師のプロフィールを統一して保持する。
/// <see cref="EntityKind"/> で種別を判別する。
/// </summary>
public sealed record JraEntityProfile(
    /// <summary>horse / jockey / trainer</summary>
    string EntityKind,
    string? DisplayName,
    /// <summary>牡・牝・セ（馬のみ）</summary>
    string? SexCode,
    DateOnly? BirthDate,
    /// <summary>美浦・栗東・地方など</summary>
    string? Affiliation,
    int? DebutYear,
    string? SireName,
    string? DamName,
    string? OwnerName,
    string? BreederName,
    string? TrainerName,
    IReadOnlyDictionary<string, string> Facts,
    string SourceUrl);
