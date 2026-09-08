using Microsoft.Playwright;

namespace HorseRacingPrediction.Scraping.Browser.Snapshots;

/// <summary>Captures the current rendered state of a Playwright page without navigating or waiting for readiness.</summary>
public interface IPageSnapshotter
{
    Task<PageSnapshot> CaptureAsync(
        IPage page,
        PageSnapshotOptions? options = null,
        CancellationToken cancellationToken = default);
}
