namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>
/// エージェントがセッション中に保持する状態。
/// 現在ページ・レースコンテキスト・戻り先スタックを管理する。
/// </summary>
public sealed class JraSessionMemory
{
    private readonly Stack<(string Url, JraPageKind Kind)> _history = new();
    private readonly HashSet<string> _failedClickTargets = new(StringComparer.Ordinal);

    // ──────────────── 現在ページ状態 ────────────────

    public string? CurrentUrl { get; private set; }
    public JraPageKind CurrentPageKind { get; private set; } = JraPageKind.Unknown;

    // ──────────────── レースコンテキスト ────────────────

    public DateOnly? CurrentRaceDate { get; private set; }
    public string? CurrentRacecourse { get; private set; }
    public int? CurrentRaceNumber { get; private set; }

    // ──────────────── 状態更新 ────────────────

    /// <summary>
    /// 現在の URL をスタックに積み、新しいページ情報を記録する。
    /// </summary>
    public void RecordNavigation(string url, JraPageKind kind)
    {
        if (!string.IsNullOrWhiteSpace(CurrentUrl))
            _history.Push((CurrentUrl, CurrentPageKind));

        CurrentUrl = url;
        CurrentPageKind = kind;
    }

    /// <summary>
    /// GoBack 後の状態に同期する（スタックから取り出す）。
    /// </summary>
    public void RecordGoBack()
    {
        if (_history.TryPop(out var prev))
        {
            CurrentUrl = prev.Url;
            CurrentPageKind = prev.Kind;
        }
        else
        {
            CurrentPageKind = JraPageKind.Unknown;
        }
    }

    /// <summary>
    /// レースコンテキストを設定する。null の引数は上書きしない。
    /// </summary>
    public void SetRaceContext(DateOnly? date, string? racecourse, int? raceNumber)
    {
        if (date.HasValue) CurrentRaceDate = date;
        if (racecourse is not null) CurrentRacecourse = racecourse;
        if (raceNumber.HasValue) CurrentRaceNumber = raceNumber;
    }

    // ──────────────── 失敗記録 ────────────────

    public void RecordFailedClick(string target) => _failedClickTargets.Add(target);
    public bool HasFailedClick(string target) => _failedClickTargets.Contains(target);
    public void ClearFailedClicks() => _failedClickTargets.Clear();

    // ──────────────── 判定ヘルパー ────────────────

    /// <summary>現在のレースコンテキストが指定の日付・競馬場・レース番号と一致するか。</summary>
    public bool IsCurrentRace(DateOnly date, string racecourse, int raceNumber)
        => CurrentRaceDate == date
           && string.Equals(CurrentRacecourse, racecourse, StringComparison.Ordinal)
           && CurrentRaceNumber == raceNumber;

    /// <summary>現在ページがレース関連ページ（出馬表・オッズ・結果）のいずれかか。</summary>
    public bool IsOnRaceRelatedPage()
        => CurrentPageKind is JraPageKind.RaceCard or JraPageKind.Odds or JraPageKind.Result;

    // ──────────────── 出馬表ページURL ────────────────

    /// <summary>
    /// 現在のレースの出馬表ページ URL。プロフィール取得後に戻る先として使用する。
    /// </summary>
    public string? CurrentRaceCardUrl { get; private set; }

    /// <summary>出馬表 URL を記録する。</summary>
    public void SetRaceCardUrl(string url) => CurrentRaceCardUrl = url;
}
