using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Agents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Agents.Browser;

/// <summary>
/// Microsoft.Playwright を使った汎用ブラウザ実装。
/// セッション中は同一の <see cref="IPage"/> を維持し、ナビゲーション・クリック・
/// テキスト取得・リンク抽出などの操作を逐次実行する。
/// </summary>
public sealed class PlaywrightWebBrowser : IWebBrowser
{
    private const string DefaultSearchBaseUrl = "https://duckduckgo.com/?q=";

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly IBrowserContext _context;
    private readonly IPage _page;
    private readonly string _searchBaseUrl;
    private readonly ILogger<PlaywrightWebBrowser> _logger;
    private bool _disposed;

    private PlaywrightWebBrowser(
        IPlaywright playwright,
        IBrowser browser,
        IBrowserContext context,
        IPage page,
        string searchBaseUrl,
        ILogger<PlaywrightWebBrowser>? logger)
    {
        _playwright = playwright;
        _browser = browser;
        _context = context;
        _page = page;
        _searchBaseUrl = string.IsNullOrWhiteSpace(searchBaseUrl)
            ? DefaultSearchBaseUrl
            : searchBaseUrl;
        _logger = logger ?? NullLogger<PlaywrightWebBrowser>.Instance;
    }

    public string? CurrentUrl
    {
        get
        {
            ThrowIfDisposed();

            var currentUrl = _page.Url;
            return string.IsNullOrWhiteSpace(currentUrl) ||
                   string.Equals(currentUrl, "about:blank", StringComparison.OrdinalIgnoreCase)
                ? null
                : currentUrl;
        }
    }

    public static async Task<PlaywrightWebBrowser> CreateAsync(
        string searchBaseUrl = DefaultSearchBaseUrl,
        BrowserTypeLaunchOptions? launchOptions = null,
        BrowserNewContextOptions? contextOptions = null,
        ILogger<PlaywrightWebBrowser>? logger = null)
    {
        var resolvedLogger = logger ?? NullLogger<PlaywrightWebBrowser>.Instance;
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(launchOptions ?? new BrowserTypeLaunchOptions
        {
            Headless = false,
            Args = [
                "--disable-gpu",
                "--no-sandbox",
                "--disable-dev-shm-usage",
                "--disable-setuid-sandbox",
                "--disable-web-security",
                "--ignore-certificate-errors",
            ]
        });

        var context = await browser.NewContextAsync(contextOptions ?? new BrowserNewContextOptions
        {
            Locale = "ja-JP",
            TimezoneId = "Asia/Tokyo",
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
        });

        var page = await context.NewPageAsync();
        resolvedLogger.LogInformation(
            "Playwright browser created. SearchBaseUrl={SearchBaseUrl} Headless={Headless}",
            string.IsNullOrWhiteSpace(searchBaseUrl) ? DefaultSearchBaseUrl : searchBaseUrl,
            (launchOptions ?? new BrowserTypeLaunchOptions { Headless = false }).Headless);

        return new PlaywrightWebBrowser(playwright, browser, context, page, searchBaseUrl, resolvedLogger);
    }

    public async Task<string> NavigateAsync(string url, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateAbsoluteUrl(url, nameof(url));

        _logger.LogInformation("Browser navigate start. Url={Url}", url);

        await _page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
        });

        await WaitForPageSettledAsync(cancellationToken);
        var content = await GetPageContentAsync(cancellationToken);
        _logger.LogInformation(
            "Browser navigate complete. Url={Url} CurrentUrl={CurrentUrl} ContentLength={ContentLength}",
            url,
            CurrentUrl,
            content.Length);
        return content;
    }

    public async Task<string> ClickAsync(string text, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("クリック対象のテキストを指定してください。", nameof(text));
        }

        _logger.LogInformation("Browser click start. Text={Text} CurrentUrl={CurrentUrl}", text, CurrentUrl);

        await WaitForPageSettledAsync(cancellationToken);

        var target = await FindClickableLocatorAsync(text, cancellationToken);
        if (target is null)
        {
            throw new InvalidOperationException($"テキスト '{text}' に一致するクリック可能要素が見つかりませんでした。");
        }

        var href = await target.GetAttributeAsync("href");
        if (TryResolveNavigableHref(CurrentUrl, href, out var resolvedHref))
        {
            _logger.LogInformation(
                "Browser click resolved to direct navigation. Text={Text} Href={Href} ResolvedHref={ResolvedHref}",
                text,
                href,
                resolvedHref);
            return await NavigateAsync(resolvedHref!, cancellationToken);
        }

        await target.ScrollIntoViewIfNeededAsync();
        await target.ClickAsync();

        if (string.IsNullOrWhiteSpace(href)
            || href.TrimStart().StartsWith('#')
            || href.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            // JRA は href="#" + onclick で same-page 更新する導線が多いため、
            // click 直後に短く待ってから DOM の安定化を確認する。
            await _page.WaitForTimeoutAsync(500);
        }

        if (string.Equals(text.Trim(), "検索", StringComparison.Ordinal)
            && await DismissHeaderSearchModalIfVisibleAsync(cancellationToken))
        {
            _logger.LogInformation(
                "Browser click dismissed header search modal after ambiguous search click. CurrentUrl={CurrentUrl}",
                CurrentUrl);
        }

        await WaitForPageSettledAsync(cancellationToken);
        var content = await GetPageContentAsync(cancellationToken);
        _logger.LogInformation(
            "Browser click complete. Text={Text} CurrentUrl={CurrentUrl} ContentLength={ContentLength}",
            text,
            CurrentUrl,
            content.Length);
        return content;
    }

    public async Task<string> SelectOptionAsync(
        string fieldText,
        string optionText,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(fieldText))
        {
            throw new ArgumentException("選択対象フィールドのラベルを指定してください。", nameof(fieldText));
        }

        if (string.IsNullOrWhiteSpace(optionText))
        {
            throw new ArgumentException("選択する値を指定してください。", nameof(optionText));
        }

        _logger.LogInformation(
            "Browser select start. Field={Field} Option={Option} CurrentUrl={CurrentUrl}",
            fieldText,
            optionText,
            CurrentUrl);

        await WaitForPageSettledAsync(cancellationToken);

        var target = await FindSelectLocatorAsync(fieldText, cancellationToken);
        if (target is null)
        {
            throw new InvalidOperationException($"ラベル '{fieldText}' に一致する選択項目が見つかりませんでした。");
        }

        await target.ScrollIntoViewIfNeededAsync();
        await target.SelectOptionAsync(new[]
        {
            new SelectOptionValue { Label = optionText },
            new SelectOptionValue { Value = optionText },
            new SelectOptionValue { Index = int.TryParse(optionText, out var index) ? index - 1 : null }
        }.Where(value => value.Label is not null || value.Value is not null || value.Index is not null).ToArray());

        await WaitForPageSettledAsync(cancellationToken);
        var content = await GetPageContentAsync(cancellationToken);
        _logger.LogInformation(
            "Browser select complete. Field={Field} Option={Option} CurrentUrl={CurrentUrl} ContentLength={ContentLength}",
            fieldText,
            optionText,
            CurrentUrl,
            content.Length);
        return content;
    }

    public async Task<string> ClickActionInSectionAsync(
        string sectionText,
        string actionText,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sectionText))
        {
            throw new ArgumentException("対象セクションの見出しを指定してください。", nameof(sectionText));
        }

        if (string.IsNullOrWhiteSpace(actionText))
        {
            throw new ArgumentException("クリックするアクションを指定してください。", nameof(actionText));
        }

        _logger.LogInformation(
            "Browser section action click start. Section={Section} Action={Action} CurrentUrl={CurrentUrl}",
            sectionText,
            actionText,
            CurrentUrl);

        await WaitForPageSettledAsync(cancellationToken);

        var target = await FindSectionActionLocatorAsync(sectionText, actionText, cancellationToken);
        if (target is null)
        {
            throw new InvalidOperationException($"セクション '{sectionText}' 内のアクション '{actionText}' が見つかりませんでした。");
        }

        await target.ScrollIntoViewIfNeededAsync();
        await target.ClickAsync();
        await _page.WaitForTimeoutAsync(500);
        await WaitForPageSettledAsync(cancellationToken);

        var content = await GetPageContentAsync(cancellationToken);
        _logger.LogInformation(
            "Browser section action click complete. Section={Section} Action={Action} CurrentUrl={CurrentUrl} ContentLength={ContentLength}",
            sectionText,
            actionText,
            CurrentUrl,
            content.Length);
        return content;
    }

    public async Task<string> GetPageContentAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await WaitForPageSettledAsync(cancellationToken);

        var rawText = await ReadPageTextAsync();
        return NormalizeText(rawText);
    }

    public async Task<IReadOnlyList<SearchResultLink>> GetLinksAsync(
        int maxResults = 0,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await WaitForPageSettledAsync(cancellationToken);

        var limit = maxResults > 0 ? maxResults : int.MaxValue;
        var links = await ExtractLinksAsync(limit, cancellationToken);
        _logger.LogInformation(
            "Browser links extracted. CurrentUrl={CurrentUrl} LinkCount={LinkCount} Limit={Limit}",
            CurrentUrl,
            links.Count,
            maxResults);
        return links;
    }

    public async Task<PageSnapshot> GetPageSnapshotAsync(
        int maxLinks = 0,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await WaitForPageSettledAsync(cancellationToken);

        var limit = maxLinks > 0 ? maxLinks : int.MaxValue;
        var url = CurrentUrl ?? string.Empty;
        var title = await TryGetPageTitleAsync();
        var mainText = NormalizeText(await ReadPageTextAsync());
        var headings = await ExtractHeadingsAsync(cancellationToken);
        var links = await ExtractLinksAsync(limit, cancellationToken);
        var actions = await ExtractActionsAsync(cancellationToken);
        var tables = await ExtractTablesAsync(cancellationToken);

        _logger.LogInformation(
            "Browser snapshot extracted. CurrentUrl={CurrentUrl} Title={Title} Headings={HeadingCount} Links={LinkCount} Actions={ActionCount} Tables={TableCount} TextLength={TextLength}",
            url,
            title,
            headings.Count,
            links.Count,
            actions.Count,
            tables.Count,
            mainText.Length);

        return new PageSnapshot(url, title, mainText, headings, links, actions, tables);
    }

    public Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("検索クエリを指定してください。", nameof(query));
        }

        var searchUrl = BuildSearchUrl(query);
        _logger.LogInformation("Browser search. Query={Query} SearchUrl={SearchUrl}", query, searchUrl);
        return NavigateAsync(searchUrl, cancellationToken);
    }

    public async Task<string> GoBackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await _page.GoBackAsync(new PageGoBackOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
        });

        await WaitForPageSettledAsync(cancellationToken);
        var content = await GetPageContentAsync(cancellationToken);
        _logger.LogInformation("Browser go back complete. CurrentUrl={CurrentUrl} ContentLength={ContentLength}", CurrentUrl, content.Length);
        return content;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        var currentUrl = CurrentUrl;
        _disposed = true;
        _logger.LogInformation("Playwright browser disposing. CurrentUrl={CurrentUrl}", currentUrl);

        try
        {
            await _context.CloseAsync();
        }
        finally
        {
            try
            {
                await _browser.CloseAsync();
            }
            finally
            {
                _playwright.Dispose();
            }
        }
    }

    private string BuildSearchUrl(string query)
    {
        var encodedQuery = Uri.EscapeDataString(query);
        return _searchBaseUrl.Contains('?', StringComparison.Ordinal)
            ? $"{_searchBaseUrl}{encodedQuery}"
            : $"{_searchBaseUrl}?q={encodedQuery}";
    }

    private async Task WaitForPageSettledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await TryWaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await TryWaitForLoadStateAsync(LoadState.Load);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task TryWaitForLoadStateAsync(LoadState state)
    {
        try
        {
            await _page.WaitForLoadStateAsync(state, new PageWaitForLoadStateOptions
            {
                Timeout = 3_000,
            });
        }
        catch (TimeoutException)
        {
            // 継続的に通信するページでも本文取得とリンク抽出を続行できるようにする。
        }
        catch (PlaywrightException)
        {
            // ナビゲーション直後の一時状態では待機に失敗しうるため、そのまま続行する。
        }
    }

    private async Task<string> ReadPageTextAsync()
    {
        var imageAltTexts = await ReadVisibleImageAltTextsAsync();

        var main = _page.Locator("main, article, [role='main']");
        if (await main.CountAsync() > 0)
        {
            for (var index = 0; index < await main.CountAsync(); index++)
            {
                var candidate = main.Nth(index);
                if (await candidate.IsVisibleAsync())
                {
                    return AppendSupplementalText(await candidate.InnerTextAsync(), imageAltTexts);
                }
            }
        }

        var body = _page.Locator("body");
        if (await body.CountAsync() > 0)
        {
            return AppendSupplementalText(await body.Nth(0).InnerTextAsync(), imageAltTexts);
        }

        var html = _page.Locator("html");
        if (await html.CountAsync() > 0)
        {
            return AppendSupplementalText(await html.Nth(0).TextContentAsync() ?? string.Empty, imageAltTexts);
        }

        return string.Empty;
    }

    private async Task<string> ReadVisibleImageAltTextsAsync()
    {
        var images = _page.Locator("img[alt]");
        if (await images.CountAsync() == 0)
        {
            return string.Empty;
        }

        var altTexts = new List<string>();
        for (var index = 0; index < await images.CountAsync(); index++)
        {
            var image = images.Nth(index);
            if (!await image.IsVisibleAsync())
            {
                continue;
            }

            var alt = await image.GetAttributeAsync("alt");
            if (!string.IsNullOrWhiteSpace(alt))
            {
                altTexts.Add(alt);
            }
        }

        return string.Join("\n", altTexts);
    }

    private static string AppendSupplementalText(string mainText, string supplementalText)
    {
        if (string.IsNullOrWhiteSpace(supplementalText))
        {
            return mainText;
        }

        return string.IsNullOrWhiteSpace(mainText)
            ? supplementalText
            : $"{mainText}\n{supplementalText}";
    }

    private async Task<string?> TryGetPageTitleAsync()
    {
        try
        {
            return NormalizeText(await _page.TitleAsync());
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> ExtractHeadingsAsync(CancellationToken cancellationToken)
    {
        var headings = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var locator = _page.Locator("h1, h2, h3");
        var count = await locator.CountAsync();

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await GetLocatorTextAsync(locator.Nth(index));
            if (string.IsNullOrWhiteSpace(text) || !seen.Add(text))
            {
                continue;
            }

            headings.Add(text);
            if (headings.Count >= 20)
            {
                break;
            }
        }

        return headings;
    }

    private async Task<IReadOnlyList<PageActionSnapshot>> ExtractActionsAsync(CancellationToken cancellationToken)
    {
        var actions = new List<PageActionSnapshot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var actionSelectors = new (string Selector, string Kind)[]
        {
            ("button", "button"),
            ("[role='button']", "button"),
            ("[role='tab']", "tab"),
            ("summary", "summary"),
            ("input[type='button'], input[type='submit']", "input")
        };

        foreach (var (selector, kind) in actionSelectors)
        {
            var locator = _page.Locator(selector);
            var count = await locator.CountAsync();
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = locator.Nth(index);
                if (!await item.IsVisibleAsync())
                {
                    continue;
                }

                var text = await GetLocatorTextAsync(item);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var key = $"{kind}:{text}";
                if (!seen.Add(key))
                {
                    continue;
                }

                actions.Add(new PageActionSnapshot(text, kind));
                if (actions.Count >= 50)
                {
                    return actions;
                }
            }
        }

        return actions;
    }

    private async Task<IReadOnlyList<PageTableSnapshot>> ExtractTablesAsync(CancellationToken cancellationToken)
    {
        var tables = new List<PageTableSnapshot>();
        var tableLocator = _page.Locator("table");
        var tableCount = await tableLocator.CountAsync();

        for (var tableIndex = 0; tableIndex < tableCount && tables.Count < 10; tableIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var table = tableLocator.Nth(tableIndex);
            if (!await table.IsVisibleAsync())
            {
                continue;
            }

            var headers = await ExtractTableHeadersAsync(table, cancellationToken);
            var rows = await ExtractTableRowsAsync(table, cancellationToken);
            if (headers.Count == 0 && rows.Count == 0)
            {
                continue;
            }

            tables.Add(new PageTableSnapshot(headers, rows));
        }

        return tables;
    }

    private async Task<IReadOnlyList<string>> ExtractTableHeadersAsync(ILocator table, CancellationToken cancellationToken)
    {
        var headers = new List<string>();
        var headerLocator = table.Locator("thead th");
        if (await headerLocator.CountAsync() == 0)
        {
            headerLocator = table.Locator("tr").First.Locator("th, td");
        }

        var count = await headerLocator.CountAsync();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await GetLocatorTextAsync(headerLocator.Nth(index));
            if (!string.IsNullOrWhiteSpace(text))
            {
                headers.Add(text);
            }
        }

        return headers;
    }

    private async Task<IReadOnlyList<IReadOnlyList<string>>> ExtractTableRowsAsync(ILocator table, CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyList<string>>();
        var rowLocator = table.Locator("tr");
        var rowCount = await rowLocator.CountAsync();

        for (var rowIndex = 0; rowIndex < rowCount && rows.Count < 10; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rowLocator.Nth(rowIndex);
            var cellLocator = row.Locator("th, td");
            var cellCount = await cellLocator.CountAsync();
            if (cellCount == 0)
            {
                continue;
            }

            var cells = new List<string>();
            for (var cellIndex = 0; cellIndex < cellCount; cellIndex++)
            {
                var text = await GetLocatorTextAsync(cellLocator.Nth(cellIndex));
                cells.Add(text);
            }

            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            rows.Add(cells);
        }

        return rows;
    }

    private async Task<ILocator?> FindClickableLocatorAsync(string text, CancellationToken cancellationToken)
    {
        var target = NormalizeForMatch(text);
        var candidates = _page.Locator("a[href], button, [role='button'], [role='link'], [role='tab'], input[type='button'], input[type='submit'], summary, [onclick]");
        var candidateCount = await candidates.CountAsync();

        ILocator? bestLocator = null;
        var bestScore = int.MaxValue;
        var bestTextLength = int.MaxValue;
        var bestRegionPriority = int.MaxValue;

        for (var index = 0; index < candidateCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = candidates.Nth(index);
            if (!await candidate.IsVisibleAsync())
            {
                continue;
            }

            var candidateText = await GetLocatorTextAsync(candidate);
            var normalizedCandidateText = NormalizeForMatch(candidateText);
            if (!normalizedCandidateText.Contains(target, StringComparison.Ordinal))
            {
                continue;
            }

            var score = normalizedCandidateText == target
                ? 0
                : normalizedCandidateText.StartsWith(target, StringComparison.Ordinal)
                    ? 1
                    : 2;

            var region = await DetermineRegionAsync(candidate);
            var regionPriority = region switch
            {
                "content" => 0,
                "header" => 1,
                "footer" => 2,
                _ => 3,
            };

            if (score < bestScore
                || (score == bestScore && regionPriority < bestRegionPriority)
                || (score == bestScore && regionPriority == bestRegionPriority && normalizedCandidateText.Length < bestTextLength))
            {
                bestLocator = candidate;
                bestScore = score;
                bestRegionPriority = regionPriority;
                bestTextLength = normalizedCandidateText.Length;
            }
        }

        return bestLocator;
    }

    private async Task<ILocator?> FindSelectLocatorAsync(string fieldText, CancellationToken cancellationToken)
    {
        var normalizedField = NormalizeForMatch(fieldText);
        var roleMatches = _page.GetByRole(AriaRole.Combobox, new PageGetByRoleOptions { Name = fieldText, Exact = false });
        if (await roleMatches.CountAsync() > 0)
        {
            return roleMatches.First;
        }

        var labelMatches = _page.GetByLabel(fieldText, new PageGetByLabelOptions { Exact = false });
        if (await labelMatches.CountAsync() > 0)
        {
            return labelMatches.First;
        }

        var candidates = _page.Locator("select");
        var candidateCount = await candidates.CountAsync();
        for (var index = 0; index < candidateCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = candidates.Nth(index);
            if (!await candidate.IsVisibleAsync())
            {
                continue;
            }

            var label = await GetLocatorTextAsync(candidate);
            if (NormalizeForMatch(label).Contains(normalizedField, StringComparison.Ordinal))
            {
                return candidate;
            }

            var ariaLabel = await candidate.GetAttributeAsync("aria-label") ?? string.Empty;
            if (NormalizeForMatch(ariaLabel).Contains(normalizedField, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<ILocator?> FindSectionActionLocatorAsync(
        string sectionText,
        string actionText,
        CancellationToken cancellationToken)
    {
        var sectionMarkers = _page.GetByText(sectionText, new PageGetByTextOptions { Exact = false });
        var markerCount = await sectionMarkers.CountAsync();
        var normalizedAction = NormalizeForMatch(actionText);

        for (var index = 0; index < markerCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var marker = sectionMarkers.Nth(index);
            if (!await marker.IsVisibleAsync())
            {
                continue;
            }

            var container = marker.Locator("xpath=ancestor::*[contains(@class,'layout_grid') or contains(@class,'setting_area') or self::section or self::form][1]");
            if (await container.CountAsync() == 0)
            {
                continue;
            }

            var candidates = container.First.Locator("a[href], button, [role='button'], input[type='button'], input[type='submit'], [onclick]");
            var candidateCount = await candidates.CountAsync();
            for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                var candidate = candidates.Nth(candidateIndex);
                if (!await candidate.IsVisibleAsync())
                {
                    continue;
                }

                var candidateText = NormalizeForMatch(await GetLocatorTextAsync(candidate));
                if (candidateText.Equals(normalizedAction, StringComparison.Ordinal)
                    || candidateText.Contains(normalizedAction, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<SearchResultLink>> ExtractLinksAsync(int limit, CancellationToken cancellationToken)
    {
        var links = new List<SearchResultLink>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await AddLinksFromSearchResultsAsync(links, seenUrls, limit, cancellationToken);
        if (links.Count >= limit)
        {
            return links;
        }

        var anchors = _page.Locator("a[href]");
        var anchorCount = await anchors.CountAsync();
        for (var index = 0; index < anchorCount && links.Count < limit; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var anchor = anchors.Nth(index);
            var link = await CreateLinkAsync(anchor);
            if (link is null || !seenUrls.Add(link.Url))
            {
                continue;
            }

            links.Add(link);
        }

        return links;
    }

    private async Task<bool> DismissHeaderSearchModalIfVisibleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var modal = _page.Locator("#modal.modal.show, #modal .modal_inner, .modal_box.modal.show, [aria-modal='true']").First;
        if (await modal.CountAsync() == 0 || !await modal.IsVisibleAsync())
        {
            return false;
        }

        var modalText = NormalizeText(await GetLocatorTextAsync(modal));
        if (!modalText.Contains("検索ウィンドウ", StringComparison.Ordinal)
            && !modalText.Contains("検索キーワード", StringComparison.Ordinal))
        {
            return false;
        }

        var closeButton = _page.GetByLabel("検索ウィンドウを閉じる", new PageGetByLabelOptions { Exact = false });
        if (await closeButton.CountAsync() == 0)
        {
            return false;
        }

        await closeButton.First.ClickAsync();
        await _page.WaitForTimeoutAsync(300);
        return true;
    }

    private async Task AddLinksFromSearchResultsAsync(
        List<SearchResultLink> links,
        HashSet<string> seenUrls,
        int limit,
        CancellationToken cancellationToken)
    {
        var currentUrl = CurrentUrl;
        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var currentUri))
        {
            return;
        }

        ILocator? resultAnchors = currentUri.Host.ToLowerInvariant() switch
        {
            var host when host.Contains("google.", StringComparison.Ordinal) => _page.Locator("#search a[href]:has(h3), #search a[href] h3").Locator("xpath=ancestor-or-self::a[1]"),
            var host when host.Contains("bing.", StringComparison.Ordinal) => _page.Locator("#b_results h2 a[href]"),
            _ => null,
        };

        if (resultAnchors is null)
        {
            return;
        }

        var resultCount = await resultAnchors.CountAsync();
        for (var index = 0; index < resultCount && links.Count < limit; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var link = await CreateLinkAsync(resultAnchors.Nth(index));
            if (link is null || !seenUrls.Add(link.Url))
            {
                continue;
            }

            links.Add(link);
        }
    }

    private async Task<SearchResultLink?> CreateLinkAsync(ILocator anchor)
    {
        var url = await anchor.GetAttributeAsync("href") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var title = await GetLocatorTextAsync(anchor);
        var region = await DetermineRegionAsync(anchor);
        return new SearchResultLink(
            url,
            string.IsNullOrWhiteSpace(title) ? url : title,
            region);
    }

    private static string NormalizeForMatch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return WhitespaceRegex.Replace(text, " ").Trim().ToLowerInvariant();
    }

    private async Task<string> GetLocatorTextAsync(ILocator locator)
    {
        string? text = null;

        try
        {
            // 短いタイムアウト (3 秒) を設定し、非表示要素で 30 秒ハングするのを防ぐ
            text = await locator.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 3000 });
        }
        catch (Exception)
        {
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            try
            {
                text = await locator.TextContentAsync(new LocatorTextContentOptions { Timeout = 3000 });
            }
            catch (Exception)
            {
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            try
            {
                var opts = new LocatorGetAttributeOptions { Timeout = 3000 };
                text = await locator.GetAttributeAsync("aria-label", opts)
                    ?? await locator.GetAttributeAsync("title", opts)
                    ?? await locator.GetAttributeAsync("value", opts);
            }
            catch (Exception)
            {
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            // 子 <img> の alt テキストをフォールバックとして使う（例: <a><img alt="1レース"></a>）
            try
            {
                var img = locator.Locator("img[alt]");
                if (await img.CountAsync() > 0)
                {
                    text = await img.First.GetAttributeAsync("alt");
                }
            }
            catch (Exception)
            {
            }
        }

        return NormalizeText(text);
    }

    private async Task<string> DetermineRegionAsync(ILocator locator)
    {
        if (await locator.Locator("xpath=ancestor::header | ancestor::*[@role='banner']").CountAsync() > 0)
        {
            return "header";
        }

        if (await locator.Locator("xpath=ancestor::footer | ancestor::*[@role='contentinfo']").CountAsync() > 0)
        {
            return "footer";
        }

        return "content";
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return WhitespaceRegex.Replace(text, " ").Trim();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void ValidateAbsoluteUrl(string url, string parameterName)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "file"))
        {
            throw new ArgumentException($"URL の形式が不正です: {url}", parameterName);
        }
    }

    private static bool TryResolveNavigableHref(string? currentUrl, string? href, out string? resolvedHref)
    {
        resolvedHref = null;

        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        var trimmedHref = href.Trim();
        if (trimmedHref.StartsWith('#')
            || trimmedHref.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Uri.TryCreate(trimmedHref, UriKind.Absolute, out var absoluteUri)
            && absoluteUri.Scheme is "http" or "https")
        {
            resolvedHref = absoluteUri.ToString();
            return true;
        }

        if (!string.IsNullOrWhiteSpace(currentUrl)
            && Uri.TryCreate(currentUrl, UriKind.Absolute, out var currentUri)
            && Uri.TryCreate(currentUri, trimmedHref, out var resolvedUri)
            && resolvedUri.Scheme is "http" or "https")
        {
            resolvedHref = resolvedUri.ToString();
            return true;
        }

        return false;
    }

}
