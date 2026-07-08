using HorseRacingPrediction.Scraping.JraNavigation;

namespace HorseRacingPrediction.Collector.Scheduling;

public interface IJraProfileLookup
{
    Task<JraExtractionEnvelope<JraEntityProfile>> GetHorseProfileAsync(
        string horseName,
        CancellationToken cancellationToken = default);

    Task<JraExtractionEnvelope<JraEntityProfile>> GetJockeyProfileAsync(
        string jockeyName,
        CancellationToken cancellationToken = default);
}