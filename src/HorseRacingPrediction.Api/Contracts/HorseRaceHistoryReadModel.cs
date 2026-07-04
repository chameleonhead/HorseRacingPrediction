namespace HorseRacingPrediction.Api.Contracts;

public sealed class HorseRaceHistoryReadModel
{
    public string HorseId { get; set; } = string.Empty;
    public List<HorseRaceHistoryEntry> Entries { get; set; } = [];

    public int TotalRaceCount => Entries.Count;
    public double WinRate => Rate(x => x.FinishPosition == 1);
    public double PlaceRate => Rate(x => x.FinishPosition is >= 1 and <= 3);
    public double RecentAvgFinishPosition => Average(
        Entries.Where(x => x.FinishPosition.HasValue).OrderByDescending(x => x.RaceDate ?? DateOnly.MinValue).Take(5).Select(x => (double?)x.FinishPosition));
    public double AvgLastThreeFurlongTime => AverageSeconds(Entries.Select(x => x.LastThreeFurlongTime));
    public double AvgPrizeMoney => Entries.Where(x => x.PrizeMoney.HasValue).Select(x => (double)x.PrizeMoney!.Value).DefaultIfEmpty().Average();
    public double WeightStabilityScore => CalculateWeightStabilityScore();
    public string? LatestJockeyId => Entries.OrderByDescending(x => x.RaceDate ?? DateOnly.MinValue).FirstOrDefault()?.JockeyId;

    public double GetAvgCornerPosition()
    {
        var values = Entries
            .Where(x => !string.IsNullOrWhiteSpace(x.CornerPositions))
            .Select(x => ParseLastCornerPosition(x.CornerPositions!))
            .Where(x => x > 0)
            .Select(x => (double)x)
            .ToList();
        return values.Count == 0 ? 0d : values.Average();
    }

    public double GetSurfaceWinRate(string surfaceCode)
    {
        var filtered = Entries.Where(x => string.Equals(x.SurfaceCode, surfaceCode, StringComparison.OrdinalIgnoreCase)).ToList();
        return Rate(filtered, x => x.FinishPosition == 1);
    }

    public double GetDistanceSuitabilityScore(int distanceMeters)
    {
        var filtered = Entries.Where(x => x.DistanceMeters.HasValue && Math.Abs(x.DistanceMeters.Value - distanceMeters) <= 200).ToList();
        return SuitabilityScore(filtered);
    }

    public double GetRacecourseSuitabilityScore(string racecourseCode)
    {
        var filtered = Entries.Where(x => string.Equals(x.RacecourseCode, racecourseCode, StringComparison.OrdinalIgnoreCase)).ToList();
        return SuitabilityScore(filtered);
    }

    public double GetDirectionSuitabilityScore(string directionCode)
    {
        var filtered = Entries.Where(x => string.Equals(x.DirectionCode, directionCode, StringComparison.OrdinalIgnoreCase)).ToList();
        return SuitabilityScore(filtered);
    }

    public int GetDaysFromLastRace(DateOnly currentRaceDate)
    {
        var latest = Entries.OrderByDescending(x => x.RaceDate).FirstOrDefault();
        return latest?.RaceDate is null ? 999 : currentRaceDate.DayNumber - latest.RaceDate.Value.DayNumber;
    }

    private double CalculateWeightStabilityScore()
    {
        var diffs = Entries.Where(x => x.DeclaredWeightDiff.HasValue).Select(x => (double)x.DeclaredWeightDiff!.Value).ToList();
        if (diffs.Count < 2)
            return 10d;

        var mean = diffs.Average();
        var variance = diffs.Select(d => (d - mean) * (d - mean)).Average();
        var stdDev = Math.Sqrt(variance);
        return Math.Max(0d, 10d - stdDev);
    }

    private double Rate(Func<HorseRaceHistoryEntry, bool> predicate) => Rate(Entries, predicate);

    private static double Rate(IEnumerable<HorseRaceHistoryEntry> source, Func<HorseRaceHistoryEntry, bool> predicate)
    {
        var list = source.ToList();
        return list.Count == 0 ? 0d : (double)list.Count(predicate) / list.Count;
    }

    private static double Average(IEnumerable<double?> values)
    {
        var list = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return list.Count == 0 ? 0d : list.Average();
    }

    private static double AverageSeconds(IEnumerable<string?> values)
    {
        var list = values
            .Select(ParseSeconds)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();
        return list.Count == 0 ? 0d : list.Average();
    }

    private static double? ParseSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return double.TryParse(value, out var seconds) ? seconds : null;
    }

    private static int ParseLastCornerPosition(string cornerPositions)
    {
        var parts = cornerPositions.Split('-');
        return parts.Length > 0 && int.TryParse(parts[^1], out var pos) ? pos : 0;
    }

    private static double SuitabilityScore(IReadOnlyCollection<HorseRaceHistoryEntry> entries)
    {
        if (entries.Count == 0)
            return 50d;

        var avgFinish = entries.Where(x => x.FinishPosition.HasValue).Select(x => x.FinishPosition!.Value).DefaultIfEmpty().Average();
        return Math.Max(0d, Math.Min(100d, (20d - avgFinish) / 20d * 100d));
    }
}