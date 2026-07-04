namespace HorseRacingPrediction.ApiClient.Contracts;

public sealed class JockeyRaceHistoryReadModel
{
    public string JockeyId { get; set; } = string.Empty;
    public List<JockeyRaceHistoryEntry> Entries { get; set; } = [];

    public int TotalRaceCount => Entries.Count;
    public double WinRate => Rate(Entries, x => x.FinishPosition == 1);
    public double PlaceRate => Rate(Entries, x => x.FinishPosition is >= 1 and <= 3);
    public double RecentWinRate => Rate(Entries.OrderByDescending(x => x.RaceDate).Take(20).ToList(), x => x.FinishPosition == 1);
    public double RecentPlaceRate => Rate(Entries.OrderByDescending(x => x.RaceDate).Take(20).ToList(), x => x.FinishPosition is >= 1 and <= 3);

    public double GetSurfaceWinRate(string surfaceCode)
    {
        var filtered = Entries.Where(x => string.Equals(x.SurfaceCode, surfaceCode, StringComparison.OrdinalIgnoreCase)).ToList();
        return Rate(filtered, x => x.FinishPosition == 1);
    }

    public double GetDistanceWinRate(int distanceMeters)
    {
        var filtered = Entries.Where(x => x.DistanceMeters.HasValue && Math.Abs(x.DistanceMeters.Value - distanceMeters) <= 200).ToList();
        return Rate(filtered, x => x.FinishPosition == 1);
    }

    public int GetHorseComboCount(string horseId) => Entries.Count(x => string.Equals(x.HorseId, horseId, StringComparison.Ordinal));

    public double GetHorseComboWinRate(string horseId)
    {
        var filtered = Entries.Where(x => string.Equals(x.HorseId, horseId, StringComparison.Ordinal)).ToList();
        return Rate(filtered, x => x.FinishPosition == 1);
    }

    private static double Rate(IReadOnlyCollection<JockeyRaceHistoryEntry> source, Func<JockeyRaceHistoryEntry, bool> predicate)
        => source.Count == 0 ? 0d : (double)source.Count(predicate) / source.Count;
}