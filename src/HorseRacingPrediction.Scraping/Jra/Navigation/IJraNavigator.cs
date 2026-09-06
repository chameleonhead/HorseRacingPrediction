using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Navigation;

/// <summary>
/// JRAサイト内のページ遷移。公開APIでは現在ページ・JRA内部URL・開催回番号等を
/// 呼び出し側へ要求しない。
/// </summary>
public interface IJraNavigator
{
    Task<IJraPage> ToKeibaTopAsync(
        CancellationToken cancellationToken = default);

    Task<IJraPage> ToCalendarAsync(
        YearMonth month,
        CancellationToken cancellationToken = default);

    Task<IJraPage> ToRaceListAsync(
        DateOnly date,
        RaceCourse course,
        CancellationToken cancellationToken = default);

    Task<IJraPage> ToRaceCardAsync(
        RaceId race,
        CancellationToken cancellationToken = default);

    Task<IJraPage> ToRaceResultAsync(
        RaceId race,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 対象日・競馬場の「レース結果 レース選択」ページ（またはそれに相当するページ）を
    /// 取得する。<see cref="ToRaceListAsync"/>（出馬表専用、掲載期間が短い）とは異なり、
    /// Current/Recent/Historicalのルート分岐を経て過去日にも対応する。
    /// </summary>
    Task<IJraPage> ToRaceResultListAsync(
        DateOnly date,
        RaceCourse course,
        CancellationToken cancellationToken = default);

    Task<IJraPage> ToHistoricalRaceSearchAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 対象日がRaceCard（出馬表）探索対象期間（依頼書3.1節の
    /// <c>RaceCardLookupPeriod</c>）内かどうかを判定する。
    ///
    /// これは「今週開催」を判定するものではなく、古いレースについて無意味に
    /// 出馬表探索（<see cref="ToRaceListAsync"/> / <see cref="ToRaceCardAsync"/>）を
    /// 試みないための最適化に過ぎない（依頼書3.1節）。最終的にRaceCardを取得するか
    /// どうかは、この期間ではなく「出馬表に対象レースが実在するか」で判定する
    /// （依頼書3.2節）。RaceResult Navigationの経路分岐（Current/Recent/Historical、
    /// ±3日/±92日）とは無関係の、完全に独立した判定であり、この5日という値を
    /// RaceResult側の分岐条件として流用してはならない（依頼書5節）。
    /// </summary>
    bool IsWithinRaceCardLookupPeriod(
        DateOnly date);
}
