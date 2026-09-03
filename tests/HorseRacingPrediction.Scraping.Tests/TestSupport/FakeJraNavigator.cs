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
}
