using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Agents.JraAgent;
using HorseRacingPrediction.Scraping.Scrapers.Jra;

namespace HorseRacingPrediction.AgentClient.JraTesting;

public sealed class JraJsonExtractionService
{
    private readonly IWebBrowserSessionFactory _browserSessionFactory;
    private readonly ILogger<JraJsonExtractionService> _logger;

    public JraJsonExtractionService(
        IWebBrowserSessionFactory browserSessionFactory,
        ILogger<JraJsonExtractionService> logger)
    {
        _browserSessionFactory = browserSessionFactory;
        _logger = logger;
    }

    public async Task<JraJsonExtractionResponse> ExtractAsync(
        string url,
        bool includeSnapshot,
        CancellationToken cancellationToken = default)
    {
        var normalizedUrl = ValidateUrl(url);

        await using var browser = await _browserSessionFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        await browser.NavigateAsync(normalizedUrl, cancellationToken).ConfigureAwait(false);

        var snapshot = await browser.GetPageSnapshotAsync(maxLinks: 20, cancellationToken: cancellationToken).ConfigureAwait(false);
        var resolvedUrl = browser.CurrentUrl ?? snapshot.Url ?? normalizedUrl;
        var pageKind = JraPageKindDetector.Detect(resolvedUrl, snapshot);
        var extraction = await ExtractCoreAsync(browser, snapshot, pageKind, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Extracted JRA page. Url={Url} ResolvedUrl={ResolvedUrl} PageKind={PageKind} Mode={Mode}",
            normalizedUrl,
            resolvedUrl,
            pageKind,
            extraction.Mode);

        return new JraJsonExtractionResponse(
            InputUrl: normalizedUrl,
            ResolvedUrl: resolvedUrl,
            PageKind: pageKind.ToString(),
            ExtractionMode: extraction.Mode,
            Title: snapshot.Title,
            Headings: snapshot.Headings,
            TableCount: snapshot.Tables.Count,
            LinkCount: snapshot.Links.Count,
            Data: ToJsonFriendlyData(extraction.Data),
            Snapshot: includeSnapshot ? snapshot : null,
            Error: extraction.Error);
    }

    private static object? ToJsonFriendlyData(object? data)
        => data switch
        {
            JraRaceCardData raceCard => new
            {
                raceCard.Url,
                raceCard.RaceName,
                raceCard.Racecourse,
                raceCard.RaceDate,
                raceCard.RaceNumber,
                raceCard.MeetingNumber,
                raceCard.DayNumber,
                raceCard.PostTime,
                raceCard.ConditionSummary,
                raceCard.AgeCondition,
                raceCard.AgeConditionCode,
                raceCard.RaceClass,
                raceCard.RaceClassCode,
                raceCard.Eligibility,
                raceCard.EligibilityCodes,
                raceCard.EntryCondition,
                raceCard.EntryConditionCodes,
                raceCard.WeightCondition,
                raceCard.WeightConditionCode,
                raceCard.CourseType,
                raceCard.TrackDirection,
                raceCard.Distance,
                raceCard.Grade,
                raceCard.PrizeMoney,
                Entries = raceCard.Entries.Select(entry => new
                {
                    entry.HorseNumber,
                    entry.GateNumber,
                    entry.HorseName,
                    entry.JockeyName,
                    AssignedWeight = entry.Weight,
                    entry.SexAge,
                    entry.BodyWeight,
                    entry.BodyWeightDiff,
                    entry.TrainerName,
                    entry.OwnerName,
                    entry.BreederName
                }).ToList()
            },
            _ => data,
        };

    private static async Task<ExtractionOutcome> ExtractCoreAsync(
        IWebBrowser browser,
        PageSnapshot snapshot,
        JraPageKind pageKind,
        CancellationToken cancellationToken)
    {
        var extractor = CreateExtractor(pageKind);
        if (extractor is not null)
        {
            var extracted = await extractor.ExtractAsync(browser, cancellationToken).ConfigureAwait(false);
            return new ExtractionOutcome("extractor", extracted, null);
        }

        if (HasStructuredParser(pageKind))
        {
            var structured = JraStructuredPageParserRegistry.Parse(pageKind, snapshot);
            return new ExtractionOutcome("structured", structured, structured.Error);
        }

        return new ExtractionOutcome(
            "snapshot",
            new
            {
                snapshot.Url,
                snapshot.Title,
                snapshot.Headings,
                snapshot.Actions,
                snapshot.Tables,
                snapshot.Links
            },
            pageKind == JraPageKind.Unknown ? "ページ種別を判定できませんでした。" : "対応する extractor / structured parser が未実装です。");
    }

    private static IPageExtractor? CreateExtractor(JraPageKind pageKind)
        => pageKind switch
        {
            JraPageKind.RaceCard => new JraRaceCardExtractor(),
            JraPageKind.Result => new JraRaceResultExtractor(),
            JraPageKind.Odds => new JraOddsExtractor(),
            JraPageKind.HorseProfile => new JraProfileExtractor(),
            JraPageKind.JockeyProfile => new JraProfileExtractor(),
            JraPageKind.TrainerProfile => new JraProfileExtractor(),
            _ => null,
        };

    private static bool HasStructuredParser(JraPageKind pageKind)
        => pageKind is JraPageKind.KeibaMenu
            or JraPageKind.ScheduleCalendar
            or JraPageKind.HoldingList
            or JraPageKind.RaceList
            or JraPageKind.ThisWeekFeature
            or JraPageKind.GradeOneSpecial
            or JraPageKind.RaceCard;

    private static string ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("url は必須です。", nameof(url));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("絶対 URL を指定してください。", nameof(url));
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("http または https の URL を指定してください。", nameof(url));
        }

        return uri.ToString();
    }

    private sealed record ExtractionOutcome(
        string Mode,
        object? Data,
        string? Error);
}