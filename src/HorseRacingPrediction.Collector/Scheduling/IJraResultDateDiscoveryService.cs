namespace HorseRacingPrediction.Collector.Scheduling;

public interface IJraResultDateDiscoveryService
{
    Task<IReadOnlyList<DateOnly>> DiscoverMonthDatesAsync(int year, int month, CancellationToken cancellationToken = default);
}