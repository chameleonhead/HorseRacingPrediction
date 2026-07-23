using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

/// <summary>
/// 騎手ごとの出走履歴を蓄積し、予測パラメーター（Group C）を提供する ReadModel。
/// JockeyId をキーに <see cref="JockeyRaceHistoryLocator"/> によって管理される。
/// </summary>
public class JockeyRaceHistoryReadModel : IReadModel,
    IAmReadModelFor<RaceAggregate, RaceId, EntryRegistered>,
    IAmReadModelFor<RaceAggregate, RaceId, EntryResultDeclared>
{
    public string JockeyId { get; private set; } = string.Empty;
    public List<JockeyRaceHistoryEntry> Entries { get; private set; } = [];

    /// <summary>
    /// 指定日時点で利用可能だった履歴だけを持つスナップショットを返す。
    /// 対象日当日の結果と学習対象レース自身は、予測時点では未知なので含めない。
    /// </summary>
    public JockeyRaceHistoryReadModel AsOf(DateOnly raceDate, string? excludedRaceId = null)
    {
        return new JockeyRaceHistoryReadModel
        {
            JockeyId = JockeyId,
            Entries = Entries
                .Where(entry =>
                    entry.RaceDate.HasValue
                    && entry.RaceDate.Value < raceDate
                    && (excludedRaceId is null
                        || !string.Equals(entry.RaceId, excludedRaceId, StringComparison.Ordinal)))
                .ToList()
        };
    }

    // ------------------------------------------------------------------ //
    // Group C: 騎手統計パラメーター
    // ------------------------------------------------------------------ //

    public int TotalRaceCount => Entries.Count(e => e.FinishPosition.HasValue);
    public int WinCount => Entries.Count(e => e.FinishPosition == 1);
    public int PlaceCount => Entries.Count(e => e.FinishPosition is >= 1 and <= 3);
    public double WinRate => TotalRaceCount == 0 ? 0 : (double)WinCount / TotalRaceCount;
    public double PlaceRate => TotalRaceCount == 0 ? 0 : (double)PlaceCount / TotalRaceCount;

    /// <summary>直近20走の勝率</summary>
    public double RecentWinRate
    {
        get
        {
            var recent = Entries
                .OrderByDescending(e => e.RaceDate ?? DateOnly.MinValue)
                .Take(20)
                .ToList();
            if (recent.Count == 0) return 0;
            return (double)recent.Count(e => e.FinishPosition == 1) / recent.Count;
        }
    }

    /// <summary>直近20走の複勝率</summary>
    public double RecentPlaceRate
    {
        get
        {
            var recent = Entries
                .OrderByDescending(e => e.RaceDate ?? DateOnly.MinValue)
                .Take(20)
                .ToList();
            if (recent.Count == 0) return 0;
            return (double)recent.Count(e => e.FinishPosition is >= 1 and <= 3) / recent.Count;
        }
    }

    /// <summary>指定馬場での騎手勝率</summary>
    public double GetSurfaceWinRate(string surfaceCode)
    {
        var filtered = Entries.Where(e => e.SurfaceCode == surfaceCode && e.FinishPosition.HasValue).ToList();
        if (filtered.Count == 0) return 0;
        return (double)filtered.Count(e => e.FinishPosition == 1) / filtered.Count;
    }

    /// <summary>指定距離±200m 帯での騎手勝率</summary>
    public double GetDistanceWinRate(int distanceMeters)
    {
        var filtered = Entries
            .Where(e => e.DistanceMeters.HasValue
                        && Math.Abs(e.DistanceMeters.Value - distanceMeters) <= 200
                        && e.FinishPosition.HasValue)
            .ToList();
        if (filtered.Count == 0) return 0;
        return (double)filtered.Count(e => e.FinishPosition == 1) / filtered.Count;
    }

    /// <summary>指定馬とのコンビ出走数</summary>
    public int GetHorseComboCount(string horseId)
        => Entries.Count(e => e.HorseId == horseId);

    /// <summary>指定馬とのコンビ勝率</summary>
    public double GetHorseComboWinRate(string horseId)
    {
        var filtered = Entries.Where(e => e.HorseId == horseId && e.FinishPosition.HasValue).ToList();
        if (filtered.Count == 0) return 0;
        return (double)filtered.Count(e => e.FinishPosition == 1) / filtered.Count;
    }

    // ------------------------------------------------------------------ //
    // IAmReadModelFor
    // ------------------------------------------------------------------ //

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, EntryRegistered> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        if (e.JockeyId == null) return Task.CompletedTask;
        JockeyId = e.JockeyId;
        Entries.Add(new JockeyRaceHistoryEntry(
            domainEvent.AggregateIdentity.Value,
            e.EntryId,
            e.HorseId,
            e.RaceDate,
            e.RacecourseCode,
            e.SurfaceCode,
            e.DistanceMeters,
            e.DirectionCode,
            e.GradeCode,
            null,
            null));
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, EntryResultDeclared> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var idx = Entries.FindIndex(x => x.EntryId == e.EntryId);
        if (idx >= 0)
        {
            Entries[idx] = Entries[idx] with
            {
                FinishPosition = e.FinishPosition,
                PrizeMoney = e.PrizeMoney
            };
        }
        return Task.CompletedTask;
    }
}
