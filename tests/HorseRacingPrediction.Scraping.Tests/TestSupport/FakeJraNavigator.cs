using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Navigation;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Tests.TestSupport;

/// <summary>
/// Workflow層のテスト用フェイク。<see cref="ToCalendarAsync"/> の戻り値のみを
/// 設定でき、他のメソッドは呼ばれた場合に失敗させる（Workflow層が使わないことの検証を兼ねる）。
/// </summary>
internal sealed class FakeJraNavigator : IJraNavigator
{
    private readonly IJraPage? _calendarResult;
    private readonly IJraPage? _raceListResult;
    private readonly Dictionary<RaceId, IJraPage> _raceCardResultsByRaceId = new();
    private readonly Dictionary<RaceId, IJraPage> _raceResultResultsByRaceId = new();
    private readonly Dictionary<(DateOnly Date, RaceCourse Course), IJraPage> _raceResultListResultsByDateCourse = new();
    private Exception? _raceListException;

    /// <summary>
    /// <see cref="ToRaceListAsync"/> が呼ばれた際に投げる例外を設定する
    /// （出馬表未公開・過去月範囲外などのシナリオをテストするためのフック）。
    /// </summary>
    public void SetRaceListException(Exception exception)
        => _raceListException = exception;

    public FakeJraNavigator(IJraPage calendarResult)
    {
        _calendarResult = calendarResult;
    }

    /// <summary>
    /// レース結果取得をテストするためのコンストラクタ。
    /// <paramref name="raceResultResultsByRaceId"/> にないレース ID で
    /// <see cref="ToRaceResultAsync"/> が呼ばれた場合は失敗する。
    /// </summary>
    public FakeJraNavigator(
        IReadOnlyDictionary<RaceId, IJraPage> raceResultResultsByRaceId)
    {
        _raceResultResultsByRaceId = new Dictionary<RaceId, IJraPage>(raceResultResultsByRaceId);
    }

    /// <summary>
    /// レース一覧・出馬表取得をテストするためのコンストラクタ。
    /// <paramref name="raceCardResultsByRaceId"/> にないレース ID で
    /// <see cref="ToRaceCardAsync"/> が呼ばれた場合は失敗する。
    /// </summary>
    public FakeJraNavigator(
        IJraPage raceListResult,
        IReadOnlyDictionary<RaceId, IJraPage> raceCardResultsByRaceId)
    {
        _raceListResult = raceListResult;
        _raceCardResultsByRaceId = new Dictionary<RaceId, IJraPage>(raceCardResultsByRaceId);
    }

    public List<YearMonth> RequestedMonths { get; } = [];

    public List<RaceId> RequestedRaceCards { get; } = [];

    public Task<IJraPage> ToKeibaTopAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IJraPage> ToCalendarAsync(YearMonth month, CancellationToken cancellationToken = default)
    {
        if (_calendarResult is null)
        {
            throw new NotSupportedException();
        }

        RequestedMonths.Add(month);
        return Task.FromResult(_calendarResult);
    }

    public Task<IJraPage> ToRaceListAsync(DateOnly date, RaceCourse course, CancellationToken cancellationToken = default)
    {
        if (_raceListException is not null)
        {
            throw _raceListException;
        }

        if (_raceListResult is null)
        {
            throw new NotSupportedException();
        }

        return Task.FromResult(_raceListResult);
    }

    public Task<IJraPage> ToRaceCardAsync(RaceId race, CancellationToken cancellationToken = default)
    {
        RequestedRaceCards.Add(race);

        if (!_raceCardResultsByRaceId.TryGetValue(race, out var page))
        {
            throw new NotSupportedException($"未設定のRaceIdです: {race}");
        }

        return Task.FromResult(page);
    }

    public List<RaceId> RequestedRaceResults { get; } = [];

    public Task<IJraPage> ToRaceResultAsync(RaceId race, CancellationToken cancellationToken = default)
    {
        RequestedRaceResults.Add(race);

        if (!_raceResultResultsByRaceId.TryGetValue(race, out var page))
        {
            throw new NotSupportedException($"未設定のRaceIdです: {race}");
        }

        return Task.FromResult(page);
    }

    public Task<IJraPage> ToHistoricalRaceSearchAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>
    /// <see cref="IsWithinRaceCardLookupPeriod"/> の戻り値。既定はtrue（常に探索対象期間内）。
    /// RaceCardLookupPeriodによる早期スキップをテストする場合はfalseに設定する。
    /// </summary>
    public bool RaceCardLookupPeriodResult { get; set; } = true;

    public bool IsWithinRaceCardLookupPeriod(DateOnly date)
        => RaceCardLookupPeriodResult;

    public List<(DateOnly Date, RaceCourse Course)> RequestedRaceResultLists { get; } = [];

    /// <summary>
    /// <see cref="ToRaceResultListAsync"/> の戻り値を(日付, 競馬場)単位で設定する。
    /// </summary>
    public void SetRaceResultListResult(DateOnly date, RaceCourse course, IJraPage page)
        => _raceResultListResultsByDateCourse[(date, course)] = page;

    public Task<IJraPage> ToRaceResultListAsync(DateOnly date, RaceCourse course, CancellationToken cancellationToken = default)
    {
        RequestedRaceResultLists.Add((date, course));

        if (_raceResultListResultsByDateCourse.TryGetValue((date, course), out var page))
        {
            return Task.FromResult(page);
        }

        if (_raceListResult is not null)
        {
            return Task.FromResult(_raceListResult);
        }

        throw new NotSupportedException($"未設定の(Date, Course)です: ({date}, {course})");
    }
}
