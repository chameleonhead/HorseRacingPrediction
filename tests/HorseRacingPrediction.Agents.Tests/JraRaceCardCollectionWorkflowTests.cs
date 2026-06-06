using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.Scraping.Scrapers.Jra;
using HorseRacingPrediction.Agents.Workflow;

namespace HorseRacingPrediction.Agents.Tests;

[TestClass]
public sealed class JraRaceCardCollectionWorkflowTests
{
    [TestMethod]
    public async Task CollectRaceAsync_RefreshesOnlyTargetRace()
    {
        var browser = new SwitchableFakeWebBrowser();
        var fakeService = new FakeDataCollectionWriteService();
        var tools = new DataCollectionWriteTools(fakeService);
        var scraper = new JraRaceCardScraper(browser);
        var workflow = new JraRaceCardCollectionWorkflow(browser, scraper, tools);

        var result = await workflow.CollectRaceAsync(new DateOnly(2026, 5, 16), "05", 1);

        Assert.HasCount(1, result.DiscoveredUrls);
        Assert.HasCount(1, result.ScrapedCards);
        Assert.HasCount(1, result.SavedRaceIds);
        Assert.IsEmpty(result.Errors);
        Assert.HasCount(1, fakeService.UpsertRaceCalls);
        Assert.HasCount(1, fakeService.UpsertRaceEntryCalls);
        Assert.AreEqual("2026-05-16", fakeService.UpsertRaceCalls[0].RaceDate);
        Assert.AreEqual("東京", fakeService.UpsertRaceCalls[0].RacecourseCode);
        Assert.AreEqual(1, fakeService.UpsertRaceCalls[0].RaceNumber);
        Assert.AreEqual("race-2026-05-16-東京-1", result.SavedRaceIds[0]);
        Assert.AreEqual(1, fakeService.UpsertRaceEntryCalls[0].HorseNumber);
        Assert.AreEqual("テストホース", fakeService.UpsertRaceEntryCalls[0].HorseName);
    }

    private sealed class FakeDataCollectionWriteService : IDataCollectionWriteService
    {
        public sealed record UpsertRaceCall(string RaceDate, string RacecourseCode, int RaceNumber, string RaceName);
        public sealed record UpsertRaceEntryCall(string RaceId, int HorseNumber, string HorseName);

        public List<UpsertRaceCall> UpsertRaceCalls { get; } = [];
        public List<UpsertRaceEntryCall> UpsertRaceEntryCalls { get; } = [];

        public Task<string> UpsertRaceAsync(
            string raceDate,
            string racecourseCode,
            int raceNumber,
            string raceName,
            int? entryCount,
            string? gradeCode,
            string? surfaceCode,
            int? distanceMeters,
            string? directionCode,
            CancellationToken cancellationToken = default)
        {
            UpsertRaceCalls.Add(new UpsertRaceCall(raceDate, racecourseCode, raceNumber, raceName));
            return Task.FromResult($"race-{raceDate}-{racecourseCode}-{raceNumber}");
        }

        public Task<string> UpsertHorseAsync(
            string registeredName,
            string? normalizedName,
            string? sexCode,
            string? birthDate,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"horse-{registeredName}");

        public Task<string> UpsertJockeyAsync(
            string displayName,
            string? normalizedName,
            string? affiliationCode,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"jockey-{displayName}");

        public Task<string> UpsertTrainerAsync(
            string displayName,
            string? normalizedName,
            string? affiliationCode,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"trainer-{displayName}");

        public Task<string> UpsertRaceEntryAsync(
            string raceId,
            int horseNumber,
            string horseName,
            string? jockeyName,
            string? trainerName,
            int? gateNumber,
            decimal? assignedWeight,
            string? sexCode,
            int? age,
            decimal? declaredWeight,
            decimal? declaredWeightDiff,
            CancellationToken cancellationToken = default)
        {
            UpsertRaceEntryCalls.Add(new UpsertRaceEntryCall(raceId, horseNumber, horseName));
            return Task.FromResult($"entry-{raceId}-{horseNumber}");
        }

        public Task<string> DeclareRaceResultAsync(
            string raceId,
            string winningHorseName,
            string? declaredAt,
            string? winningHorseId,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"result-{raceId}");

        public Task<string> DeclareRaceEntryResultAsync(
            string raceId,
            int horseNumber,
            int? finishPosition,
            string? officialTime,
            string? marginText,
            string? lastThreeFurlongTime,
            string? abnormalResultCode,
            decimal? prizeMoney,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"entry-result-{raceId}-{horseNumber}");

        public Task<string> DeclareRacePayoutsAsync(
            string raceId,
            string? winPayoutsJson,
            string? placePayoutsJson,
            string? quinellaPayoutsJson,
            string? exactaPayoutsJson,
            string? trifectaPayoutsJson,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"payout-{raceId}");
    }

    private sealed class SwitchableFakeWebBrowser : IWebBrowser
    {
        private const string DiscoveryUrl = "https://www.jra.go.jp/keiba/thisweek/";
        private const string CardUrl = "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01sde0203_20260516050101";

        private readonly PageSnapshot _discoverySnapshot = new(
            Url: DiscoveryUrl,
            Title: "2026年5月16日 JRA",
            MainText: "2026年5月16日 東京 1R",
            Headings: ["2026年5月16日 東京 1R"],
            Links: [new PageLinkSnapshot(CardUrl, "1R")],
            Actions: [],
            Tables: []);

        private readonly PageSnapshot _cardSnapshot = new(
            Url: CardUrl,
            Title: "1R 出馬表",
            MainText: "東京 芝 1600メートル",
            Headings: ["2026年5月16日（土曜） 2回東京1日", "1レース"],
            Links: [],
            Actions: [],
            Tables: [new PageTableSnapshot(
                ["馬番", "馬名", "騎手", "調教師", "性齢", "斤量", "枠番", "馬体重"],
                [
                    ["1", "テストホース", "テスト騎手", "テスト調教師", "牡3", "55.0", "1", "480(+2)"]
                ])]);

        public string? CurrentUrl { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default)
        {
            CurrentUrl = url;
            return Task.FromResult(string.Empty);
        }

        public Task<string> ClickAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> SelectOptionAsync(string fieldText, string optionText, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> ClickActionInSectionAsync(string sectionText, string actionText, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> GetPageContentAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<PageSnapshot> GetPageSnapshotAsync(int maxLinks = 0, CancellationToken cancellationToken = default)
            => Task.FromResult(IsDiscoveryPage ? _discoverySnapshot : _cardSnapshot);

        public Task<IReadOnlyList<PageLinkSnapshot>> GetLinksAsync(int maxResults = 10, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PageLinkSnapshot>>(IsDiscoveryPage ? _discoverySnapshot.Links : []);

        public Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> GoBackAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        private bool IsDiscoveryPage => string.IsNullOrWhiteSpace(CurrentUrl) || CurrentUrl.Contains("thisweek", StringComparison.OrdinalIgnoreCase);
    }
}