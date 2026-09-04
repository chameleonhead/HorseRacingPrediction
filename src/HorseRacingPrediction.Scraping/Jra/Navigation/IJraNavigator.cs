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
}
