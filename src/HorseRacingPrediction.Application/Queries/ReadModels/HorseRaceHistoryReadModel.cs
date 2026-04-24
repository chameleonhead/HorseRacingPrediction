using EventFlow.Aggregates;
using EventFlow.ReadStores;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Queries.ReadModels;

/// <summary>
/// 馬ごとの出走履歴を蓄積し、予測パラメーター（Group B）を提供する ReadModel。
/// HorseId をキーに <see cref="HorseRaceHistoryLocator"/> によって管理される。
/// </summary>
public class HorseRaceHistoryReadModel : IReadModel,
    IAmReadModelFor<RaceAggregate, RaceId, EntryRegistered>,
    IAmReadModelFor<RaceAggregate, RaceId, EntryResultDeclared>
{
    private readonly List<HorseRaceHistoryEntry> _entries = new();

    public string HorseId { get; private set; } = string.Empty;
    public IReadOnlyList<HorseRaceHistoryEntry> Entries => _entries.AsReadOnly();

    // ------------------------------------------------------------------ //
    // Group B: 基本集計パラメーター
    // ------------------------------------------------------------------ //

    /// <summary>総出走数（結果判明分）</summary>
    public int TotalRaceCount => _entries.Count(e => e.FinishPosition.HasValue);

    /// <summary>勝利数</summary>
    public int WinCount => _entries.Count(e => e.FinishPosition == 1);

    /// <summary>複勝数（3着以内）</summary>
    public int PlaceCount => _entries.Count(e => e.FinishPosition is >= 1 and <= 3);

    /// <summary>勝率</summary>
    public double WinRate => TotalRaceCount == 0 ? 0 : (double)WinCount / TotalRaceCount;

    /// <summary>複勝率</summary>
    public double PlaceRate => TotalRaceCount == 0 ? 0 : (double)PlaceCount / TotalRaceCount;

    /// <summary>直近5走の平均着順</summary>
    public double RecentAvgFinishPosition
    {
        get
        {
            var recent = _entries
                .Where(e => e.FinishPosition.HasValue)
                .OrderByDescending(e => e.RaceDate ?? DateOnly.MinValue)
                .Take(5)
                .Select(e => e.FinishPosition!.Value)
                .ToList();
            return recent.Count == 0 ? 0 : recent.Average();
        }
    }

    /// <summary>平均上がり3Fタイム（秒）</summary>
    public double AvgLastThreeFurlongTime
    {
        get
        {
            var times = _entries
                .Where(e => e.LastThreeFurlongTime != null)
                .Select(e => ParseTimeToSeconds(e.LastThreeFurlongTime!))
                .Where(t => t > 0)
                .ToList();
            return times.Count == 0 ? 0 : times.Average();
        }
    }

    /// <summary>平均賞金</summary>
    public double AvgPrizeMoney
    {
        get
        {
            var prizes = _entries
                .Where(e => e.PrizeMoney.HasValue)
                .Select(e => (double)e.PrizeMoney!.Value)
                .ToList();
            return prizes.Count == 0 ? 0 : prizes.Average();
        }
    }

    /// <summary>体重安定度スコア（0〜10、標準偏差が小さいほど高い）</summary>
    public double WeightStabilityScore
    {
        get
        {
            var diffs = _entries
                .Where(e => e.DeclaredWeightDiff.HasValue)
                .Select(e => (double)e.DeclaredWeightDiff!.Value)
                .ToList();
            if (diffs.Count < 2) return 10.0;
            var mean = diffs.Average();
            var variance = diffs.Select(d => (d - mean) * (d - mean)).Average();
            var stdDev = Math.Sqrt(variance);
            return Math.Max(0, 10.0 - stdDev);
        }
    }

    /// <summary>直近レース日</summary>
    public DateOnly? LatestRaceDate =>
        _entries
            .Where(e => e.RaceDate.HasValue)
            .OrderByDescending(e => e.RaceDate)
            .FirstOrDefault()?.RaceDate;

    /// <summary>直近騎手 ID</summary>
    public string? LatestJockeyId =>
        _entries
            .OrderByDescending(e => e.RaceDate ?? DateOnly.MinValue)
            .FirstOrDefault()?.JockeyId;

    // ------------------------------------------------------------------ //
    // Group B: 馬場・距離・競馬場・回り 適性スコア
    // ------------------------------------------------------------------ //

    /// <summary>指定馬場での勝率</summary>
    public double GetSurfaceWinRate(string surfaceCode)
    {
        var filtered = _entries.Where(e => e.SurfaceCode == surfaceCode && e.FinishPosition.HasValue).ToList();
        if (filtered.Count == 0) return 0;
        return (double)filtered.Count(e => e.FinishPosition == 1) / filtered.Count;
    }

    /// <summary>
    /// 指定距離±200m 帯の成績から距離適性スコア（0〜100）を算出。
    /// 平均着順が低いほど高スコア。
    /// </summary>
    public double GetDistanceSuitabilityScore(int distanceMeters)
    {
        var filtered = _entries
            .Where(e => e.DistanceMeters.HasValue
                        && Math.Abs(e.DistanceMeters.Value - distanceMeters) <= 200
                        && e.FinishPosition.HasValue)
            .ToList();
        if (filtered.Count == 0) return 50.0;
        var avgFinish = filtered.Average(e => e.FinishPosition!.Value);
        return Math.Max(0, Math.Min(100, (20.0 - avgFinish) / 20.0 * 100.0));
    }

    /// <summary>指定競馬場での成績から競馬場適性スコア（0〜100）を算出。</summary>
    public double GetRacecourseSuitabilityScore(string racecourseCode)
    {
        var filtered = _entries
            .Where(e => e.RacecourseCode == racecourseCode && e.FinishPosition.HasValue)
            .ToList();
        if (filtered.Count == 0) return 50.0;
        var avgFinish = filtered.Average(e => e.FinishPosition!.Value);
        return Math.Max(0, Math.Min(100, (20.0 - avgFinish) / 20.0 * 100.0));
    }

    /// <summary>指定回りでの成績から回り適性スコア（0〜100）を算出。</summary>
    public double GetDirectionSuitabilityScore(string directionCode)
    {
        var filtered = _entries
            .Where(e => e.DirectionCode == directionCode && e.FinishPosition.HasValue)
            .ToList();
        if (filtered.Count == 0) return 50.0;
        var avgFinish = filtered.Average(e => e.FinishPosition!.Value);
        return Math.Max(0, Math.Min(100, (20.0 - avgFinish) / 20.0 * 100.0));
    }

    /// <summary>
    /// コーナー通過順文字列（例: "2-3-3-4"）の末尾番号（最終コーナー順位）の平均。
    /// </summary>
    public double GetAvgCornerPosition()
    {
        var positions = _entries
            .Where(e => e.CornerPositions != null)
            .Select(e => ParseLastCornerPosition(e.CornerPositions!))
            .Where(p => p > 0)
            .ToList();
        return positions.Count == 0 ? 0 : positions.Average();
    }

    /// <summary>指定レース日から前走までの間隔（日数）。前走がない場合は 999 を返す。</summary>
    public int GetDaysFromLastRace(DateOnly currentRaceDate)
    {
        if (!LatestRaceDate.HasValue) return 999;
        return currentRaceDate.DayNumber - LatestRaceDate.Value.DayNumber;
    }

    // ------------------------------------------------------------------ //
    // IAmReadModelFor
    // ------------------------------------------------------------------ //

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, EntryRegistered> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        HorseId = e.HorseId;
        _entries.Add(new HorseRaceHistoryEntry(
            domainEvent.AggregateIdentity.Value,
            e.EntryId,
            e.RaceDate,
            e.RacecourseCode,
            e.SurfaceCode,
            e.DistanceMeters,
            e.DirectionCode,
            e.GradeCode,
            e.GateNumber,
            e.AssignedWeight,
            e.DeclaredWeight,
            e.DeclaredWeightDiff,
            e.RunningStyleCode,
            e.JockeyId,
            e.TrainerId,
            null, null, null, null));
        return Task.CompletedTask;
    }

    public Task ApplyAsync(IReadModelContext context,
        IDomainEvent<RaceAggregate, RaceId, EntryResultDeclared> domainEvent,
        CancellationToken cancellationToken)
    {
        var e = domainEvent.AggregateEvent;
        var idx = _entries.FindIndex(x => x.EntryId == e.EntryId);
        if (idx >= 0)
        {
            _entries[idx] = _entries[idx] with
            {
                FinishPosition = e.FinishPosition,
                LastThreeFurlongTime = e.LastThreeFurlongTime,
                CornerPositions = e.CornerPositions,
                PrizeMoney = e.PrizeMoney
            };
        }
        return Task.CompletedTask;
    }

    private static double ParseTimeToSeconds(string timeStr)
    {
        if (double.TryParse(timeStr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        return 0;
    }

    private static int ParseLastCornerPosition(string cornerPositions)
    {
        var parts = cornerPositions.Split('-');
        if (parts.Length > 0 && int.TryParse(parts[^1], out var pos))
            return pos;
        return 0;
    }
}
