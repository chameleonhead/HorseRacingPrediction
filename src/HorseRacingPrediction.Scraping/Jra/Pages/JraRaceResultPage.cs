using HorseRacingPrediction.Scraping.Jra.Models;

namespace HorseRacingPrediction.Scraping.Jra.Pages;

public sealed record JraRaceResultPage(
    string Url,
    RaceId RaceId,
    string? RaceName,
    IReadOnlyList<RaceResultEntry> Results,
    string? WeatherText = null,
    string? TrackConditionText = null,
    RacePayouts? Payouts = null,
    RaceCourseSpec? CourseSpec = null,
    IReadOnlyList<CornerPassage>? CornerPassages = null,
    // Phase8: レース全体の「タイム」欄（上り集計、例:「上り: 1マイル1分48秒1 4F 51.3 - 3F 38.5」）。
    // フォーマット解析難易度が高いため、数値分解はせず生文字列のまま保持する。
    string? OverallPaceText = null,
    // Phase8（依頼書27節）: 本賞金。着順(1着=1等)をキーに円単位の金額を保持する。
    // 「本賞金」欄自体が存在しない場合はnull（正常）。
    IReadOnlyDictionary<int, decimal>? PrizeMoneyByPosition = null,
    string? GradeCode = null,
    TimeOnly? StartTime = null,
    int? MeetingNumber = null,
    int? MeetingDay = null,
    string? RaceConditions = null,
    IReadOnlyList<string>? SectionalTimes = null)
    : IJraPage
{
    public JraPageKind Kind =>
        JraPageKind.RaceResult;
}

public sealed record PayoutLine(string Combination, decimal Amount, int? Popularity = null);

public sealed record RacePayouts(
    IReadOnlyList<PayoutLine> WinPayouts,
    IReadOnlyList<PayoutLine> PlacePayouts,
    IReadOnlyList<PayoutLine> QuinellaPayouts,
    IReadOnlyList<PayoutLine> ExactaPayouts,
    IReadOnlyList<PayoutLine> TrifectaPayouts,
    // Phase8: 実サイトの払戻金欄には枠連・ワイド・3連複も存在することが確認できた
    // ため追加。命名は既存のWin/Place/Quinella(=馬連)/Exacta(=馬単)/Trifecta(=3連単)
    // という英語命名パターンに合わせる。
    IReadOnlyList<PayoutLine>? BracketQuinellaPayouts = null,
    IReadOnlyList<PayoutLine>? WidePayouts = null,
    IReadOnlyList<PayoutLine>? TrioPayouts = null)
{
    public IReadOnlyList<PayoutLine> BracketQuinellaPayoutsOrEmpty => BracketQuinellaPayouts ?? [];

    public IReadOnlyList<PayoutLine> WidePayoutsOrEmpty => WidePayouts ?? [];

    public IReadOnlyList<PayoutLine> TrioPayoutsOrEmpty => TrioPayouts ?? [];

    public bool IsEmpty =>
        WinPayouts.Count == 0 && PlacePayouts.Count == 0 && QuinellaPayouts.Count == 0 &&
        ExactaPayouts.Count == 0 && TrifectaPayouts.Count == 0 &&
        BracketQuinellaPayoutsOrEmpty.Count == 0 && WidePayoutsOrEmpty.Count == 0 &&
        TrioPayoutsOrEmpty.Count == 0;
}
