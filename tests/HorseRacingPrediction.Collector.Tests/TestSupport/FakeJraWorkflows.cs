using HorseRacingPrediction.Scraping.Jra;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Workflow;

namespace HorseRacingPrediction.Collector.Tests.TestSupport;

internal sealed class FakeJraScheduleCollectionWorkflow : IJraScheduleCollectionWorkflow
{
    public Func<DateOnly, IReadOnlyList<RaceCourse>>? CoursesByDate { get; set; }

    public Exception? ThrowOnCollect { get; set; }

    public List<DateOnly> Requests { get; } = new();

    public Task<IReadOnlyList<RaceCourse>> CollectAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        Requests.Add(date);
        if (ThrowOnCollect is not null)
        {
            throw ThrowOnCollect;
        }

        var courses = CoursesByDate?.Invoke(date) ?? Array.Empty<RaceCourse>();
        return Task.FromResult(courses);
    }
}

internal sealed class FakeJraRaceCardCollectionWorkflow : IJraRaceCardCollectionWorkflow
{
    public Func<DateOnly, RaceCourse, RaceCardCollectionResult>? ResultFactory { get; set; }

    public Exception? ThrowOnCollect { get; set; }

    public List<(DateOnly Date, RaceCourse Course)> Requests { get; } = new();

    public Task<RaceCardCollectionResult> CollectAsync(DateOnly date, RaceCourse course, CancellationToken cancellationToken = default)
    {
        Requests.Add((date, course));
        if (ThrowOnCollect is not null)
        {
            throw ThrowOnCollect;
        }

        var result = ResultFactory?.Invoke(date, course)
            ?? new RaceCardCollectionResult(date, course, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<RaceCardRaceOutcome>());
        return Task.FromResult(result);
    }
}

internal sealed class FakeJraRaceResultCollectionWorkflow : IJraRaceResultCollectionWorkflow
{
    public Func<RaceId, RaceResultCollectionResult>? ResultFactory { get; set; }

    public Exception? ThrowOnCollect { get; set; }

    public List<RaceId> Requests { get; } = new();

    public List<bool> UseSiblingNavigationRequests { get; } = new();

    public Task<RaceResultCollectionResult> CollectAsync(RaceId raceId, CancellationToken cancellationToken = default)
        => CollectAsync(raceId, useSiblingNavigation: false, cancellationToken);

    public Task<RaceResultCollectionResult> CollectAsync(RaceId raceId, bool useSiblingNavigation, CancellationToken cancellationToken = default)
    {
        Requests.Add(raceId);
        UseSiblingNavigationRequests.Add(useSiblingNavigation);
        if (ThrowOnCollect is not null)
        {
            throw ThrowOnCollect;
        }

        var result = ResultFactory?.Invoke(raceId)
            ?? new RaceResultCollectionResult(raceId, $"race-{raceId.Date:yyyyMMdd}-{raceId.Course}-{raceId.Number}", Array.Empty<int>(), Array.Empty<string>());
        return Task.FromResult(result);
    }
}
