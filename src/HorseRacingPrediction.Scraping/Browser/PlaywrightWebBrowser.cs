using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace HorseRacingPrediction.Scraping.Browser;

/// <summary>
/// Microsoft.Playwright を使った汎用ブラウザ実装。
/// セッション中は同一の <see cref="IPage"/> を維持し、ナビゲーション・クリック・
/// テキスト取得・リンク抽出などの操作を逐次実行する。
/// </summary>
public sealed class PlaywrightWebBrowser : IWebBrowser
{
    private const string DefaultSearchBaseUrl = "https://duckduckgo.com/?q=";
    private const int MaxSnapshotSectionCount = 24;
    private const int MaxSectionTextLength = 4000;
    private const int MaxLinksPerSection = 80;
    private const int MaxActionsPerSection = 80;
    private const int MaxTablesPerSection = 8;
    private const int MaxFormsPerSection = 12;
    private const int MaxImagesPerSection = 40;
    private const int MaxSnapshotTableCount = 10;
    private const int MaxSnapshotRowsPerTable = 60;
    private const int MinSectionTextLength = 24;
    private const int MergeCompactSectionTextThreshold = 140;
    private const int MaxMergedCompactSections = 4;
    private const string HeaderSectionSelector = "header, [role='banner'], div[id='header' i], div[id$='-header' i], div[id*='_header' i], nav[id='header' i], nav[id$='-header' i], nav[id*='_header' i], section[id='header' i], section[id$='-header' i], section[id*='_header' i], div[class~='header' i], div[class^='header-' i], div[class*='-header' i], div[class*='_header' i], nav[class~='header' i], nav[class^='header-' i], nav[class*='-header' i], nav[class*='_header' i], section[class~='header' i], section[class^='header-' i], section[class*='-header' i], section[class*='_header' i]";
    private const string FooterSectionSelector = "footer, [role='contentinfo'], div[id='footer' i], div[id$='-footer' i], div[id*='_footer' i], nav[id='footer' i], nav[id$='-footer' i], nav[id*='_footer' i], section[id='footer' i], section[id$='-footer' i], section[id*='_footer' i], div[class~='footer' i], div[class^='footer-' i], div[class*='-footer' i], div[class*='_footer' i], nav[class~='footer' i], nav[class^='footer-' i], nav[class*='-footer' i], nav[class*='_footer' i], section[class~='footer' i], section[class^='footer-' i], section[class*='-footer' i], section[class*='_footer' i]";

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
            Headless = true,
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
        }).WaitAsync(cancellationToken);

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
        await target.ClickAsync().WaitAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(href)
            || href.TrimStart().StartsWith('#')
            || href.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            // JRA は href="#" + onclick で same-page 更新する導線が多いため、
            // click 直後に短く待ってから DOM の安定化を確認する。
            await _page.WaitForTimeoutAsync(500).WaitAsync(cancellationToken);
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

    public async Task<IReadOnlyList<PageLinkSnapshot>> GetLinksAsync(
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
        var title = await TryGetPageTitleAsync() ?? string.Empty;
        var sections = await ExtractSectionsAsync(title, limit, cancellationToken);
        var totalTextLength = sections.Sum(section => section.MainText.Length);

        _logger.LogInformation(
            "Browser snapshot extracted. CurrentUrl={CurrentUrl} Title={Title} Sections={SectionCount} DeferredCollections=true TextLength={TextLength}",
            url,
            title,
            sections.Count,
            totalTextLength);

        return new PageSnapshot(url, title, sections);
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

    public async Task<IReadOnlyList<PageFormSnapshot>> GetFormsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await WaitForPageSettledAsync(cancellationToken);

        var forms = new List<PageFormSnapshot>();
        var formLocator = _page.Locator("form");
        var formCount = await formLocator.CountAsync();

        for (var formIndex = 0; formIndex < formCount; formIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var form = formLocator.Nth(formIndex);
            if (!await form.IsVisibleAsync())
            {
                continue;
            }

            var title = await ExtractFormTitleAsync(form, formIndex);
            var action = await form.GetAttributeAsync("action") ?? string.Empty;
            var method = (await form.GetAttributeAsync("method") ?? "GET").ToUpperInvariant();
            var fields = await ExtractFormFieldsAsync(form, cancellationToken);

            forms.Add(new PageFormSnapshot(title, action, method, fields));
        }

        return forms;
    }

    public async Task<string> SetFieldValueAsync(
        string fieldLabelOrName,
        string value,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(fieldLabelOrName))
        {
            throw new ArgumentException("入力対象フィールド名を指定してください。", nameof(fieldLabelOrName));
        }

        var field = await FindFillableFieldAsync(fieldLabelOrName, cancellationToken);
        if (field is null)
        {
            throw new InvalidOperationException($"フィールド '{fieldLabelOrName}' が見つかりませんでした。");
        }

        await field.ScrollIntoViewIfNeededAsync();
        await field.FillAsync(value ?? string.Empty).WaitAsync(cancellationToken);
        await WaitForPageSettledAsync(cancellationToken);
        return await GetPageContentAsync(cancellationToken);
    }

    public async Task<string> SetCheckboxAsync(
        string fieldLabelOrName,
        bool isChecked,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(fieldLabelOrName))
        {
            throw new ArgumentException("チェック対象フィールド名を指定してください。", nameof(fieldLabelOrName));
        }

        var checkbox = await FindCheckboxAsync(fieldLabelOrName, cancellationToken);
        if (checkbox is null)
        {
            throw new InvalidOperationException($"チェックボックス '{fieldLabelOrName}' が見つかりませんでした。");
        }

        await checkbox.ScrollIntoViewIfNeededAsync();
        if (isChecked)
        {
            await checkbox.CheckAsync().WaitAsync(cancellationToken);
        }
        else
        {
            await checkbox.UncheckAsync().WaitAsync(cancellationToken);
        }

        await WaitForPageSettledAsync(cancellationToken);
        return await GetPageContentAsync(cancellationToken);
    }

    public async Task<string> SubmitFormAsync(
        string? formLabel = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var form = await FindFormAsync(formLabel, cancellationToken);
        if (form is null)
        {
            throw new InvalidOperationException("送信対象のフォームが見つかりませんでした。");
        }

        var submitButtons = form.Locator("button[type='submit'], input[type='submit']");
        if (await submitButtons.CountAsync() > 0)
        {
            var button = submitButtons.First;
            await button.ScrollIntoViewIfNeededAsync();
            await button.ClickAsync().WaitAsync(cancellationToken);
        }
        else
        {
            // submit ボタンがないフォーム向けに requestSubmit を実行する。
            await form.EvaluateAsync("form => form.requestSubmit ? form.requestSubmit() : form.submit()").WaitAsync(cancellationToken);
        }

        await WaitForPageSettledAsync(cancellationToken);
        return await GetPageContentAsync(cancellationToken);
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
                if (await IsElementRenderedAsync(candidate))
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
            if (!await IsElementRenderedAsync(image))
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
        return await ExtractHeadingsFromRootAsync(_page.Locator("body"), 20, cancellationToken);
    }

    private async Task<string> ExtractFormTitleAsync(ILocator form, int index)
    {
        var legend = form.Locator("legend").First;
        if (await legend.CountAsync() > 0)
        {
            var legendText = await GetLocatorTextAsync(legend);
            if (!string.IsNullOrWhiteSpace(legendText))
            {
                return legendText;
            }
        }

        var ariaLabel = await form.GetAttributeAsync("aria-label");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
        {
            return NormalizeText(ariaLabel);
        }

        return $"Form {index + 1}";
    }

    private async Task<IReadOnlyList<PageFormFieldSnapshot>> ExtractFormFieldsAsync(ILocator form, CancellationToken cancellationToken)
    {
        var fields = new List<PageFormFieldSnapshot>();
        var fieldLocator = form.Locator("input, textarea, select");
        var fieldCount = await fieldLocator.CountAsync();

        for (var index = 0; index < fieldCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var field = fieldLocator.Nth(index);
            if (!await IsElementRenderedAsync(field))
            {
                continue;
            }

            var tagName = (await field.EvaluateAsync<string>("el => el.tagName.toLowerCase()")) ?? string.Empty;
            var type = (await field.GetAttributeAsync("type") ?? string.Empty).ToLowerInvariant();
            var kind = ResolveFieldKind(tagName, type);

            var name = await field.GetAttributeAsync("name") ?? string.Empty;
            var id = await field.GetAttributeAsync("id") ?? string.Empty;
            var required = await field.EvaluateAsync<bool>("el => !!el.required || el.getAttribute('aria-required') === 'true'");
            var disabled = await field.EvaluateAsync<bool>("el => !!el.disabled || el.getAttribute('aria-disabled') === 'true'");
            var placeholder = await field.GetAttributeAsync("placeholder");
            var value = await field.InputValueAsync();

            var label = await ResolveFieldLabelAsync(field, id, name);
            var options = kind == PageFormFieldKind.Select
                ? await ExtractSelectOptionsAsync(field)
                : [];

            fields.Add(new PageFormFieldSnapshot(label, name, kind, required, disabled, placeholder, value, options));
        }

        return fields;
    }

    private static PageFormFieldKind ResolveFieldKind(string tagName, string type)
    {
        return (tagName, type) switch
        {
            ("textarea", _) => PageFormFieldKind.TextArea,
            ("select", _) => PageFormFieldKind.Select,
            (_, "checkbox") => PageFormFieldKind.Checkbox,
            (_, "radio") => PageFormFieldKind.Radio,
            ("input", _) => PageFormFieldKind.Text,
            _ => PageFormFieldKind.Unknown,
        };
    }

    private async Task<string> ResolveFieldLabelAsync(ILocator field, string id, string name)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            var label = _page.Locator($"label[for='{EscapeForCss(id)}']").First;
            if (await label.CountAsync() > 0)
            {
                var labelText = await GetLocatorTextAsync(label);
                if (!string.IsNullOrWhiteSpace(labelText))
                {
                    return labelText;
                }
            }
        }

        var ariaLabel = await field.GetAttributeAsync("aria-label");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
        {
            return NormalizeText(ariaLabel);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return string.Empty;
    }

    private async Task<IReadOnlyList<string>> ExtractSelectOptionsAsync(ILocator field)
    {
        var options = new List<string>();
        var optionLocator = field.Locator("option");
        var count = await optionLocator.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var text = await GetLocatorTextAsync(optionLocator.Nth(i));
            if (!string.IsNullOrWhiteSpace(text))
            {
                options.Add(text);
            }
        }

        return options;
    }

    private async Task<ILocator?> FindFillableFieldAsync(string fieldLabelOrName, CancellationToken cancellationToken)
    {
        var byLabel = _page.GetByLabel(fieldLabelOrName, new PageGetByLabelOptions { Exact = false });
        if (await byLabel.CountAsync() > 0)
        {
            return byLabel.First;
        }

        var escaped = EscapeForCss(fieldLabelOrName);
        var byName = _page.Locator($"input[name='{escaped}'], textarea[name='{escaped}'], select[name='{escaped}']");
        if (await byName.CountAsync() > 0)
        {
            return byName.First;
        }

        var byPlaceholder = _page.Locator($"input[placeholder*='{escaped}'], textarea[placeholder*='{escaped}']");
        if (await byPlaceholder.CountAsync() > 0)
        {
            return byPlaceholder.First;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private async Task<ILocator?> FindCheckboxAsync(string fieldLabelOrName, CancellationToken cancellationToken)
    {
        var byLabel = _page.GetByLabel(fieldLabelOrName, new PageGetByLabelOptions { Exact = false });
        if (await byLabel.CountAsync() > 0)
        {
            return byLabel.First;
        }

        var escaped = EscapeForCss(fieldLabelOrName);
        var byName = _page.Locator($"input[type='checkbox'][name='{escaped}']");
        if (await byName.CountAsync() > 0)
        {
            return byName.First;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private async Task<ILocator?> FindFormAsync(string? formLabel, CancellationToken cancellationToken)
    {
        var forms = _page.Locator("form");
        var count = await forms.CountAsync();
        if (count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(formLabel))
        {
            for (var i = 0; i < count; i++)
            {
                var form = forms.Nth(i);
                if (await IsElementRenderedAsync(form))
                {
                    return form;
                }
            }

            return forms.First;
        }

        var normalizedTarget = NormalizeForMatch(formLabel);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var form = forms.Nth(i);
            if (!await IsElementRenderedAsync(form))
            {
                continue;
            }

            var title = NormalizeForMatch(await ExtractFormTitleAsync(form, i));
            if (title.Contains(normalizedTarget, StringComparison.Ordinal))
            {
                return form;
            }
        }

        return null;
    }

    private static string EscapeForCss(string value)
        => value.Replace("'", "\\'", StringComparison.Ordinal);

    private static async Task<bool> IsElementRenderedAsync(ILocator locator)
    {
        if (await locator.CountAsync() == 0)
        {
            return false;
        }

        if (!await locator.IsVisibleAsync())
        {
            return false;
        }

        return true;
    }

    private async Task<List<PageSectionSnapshot>> ExtractSectionsAsync(
        string pageTitle,
        int linkLimit,
        CancellationToken cancellationToken)
    {
        var sections = new List<PageSectionSnapshot>(capacity: Math.Min(MaxSnapshotSectionCount, 12));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var boundedLinkLimit = BoundLimit(linkLimit, MaxLinksPerSection);

        // まずはヘッダー・フッターを固定的に抽出しておく。
        await AddSpecialLayoutSectionAsync(
            selector: HeaderSectionSelector,
            sections,
            seen,
            boundedLinkLimit,
            cancellationToken,
            fallbackTitle: "Header");

        var sectionCountAfterHeader = sections.Count;

        await AddSectionsFromCandidatesAsync(
            selector: "main section, article section, [role='region'], section, article",
            sections,
            seen,
            boundedLinkLimit,
            cancellationToken,
            enforceQualityThreshold: true);

        // セマンティック要素が少ないサイト向けフォールバック。
        if (sections.Count < 3)
        {
            await AddSectionsFromCandidatesAsync(
                selector: "[role='main'] > div, main > div, article > div, [data-testid], [class*='card'], [class*='post'], [class*='article']",
                sections,
                seen,
                boundedLinkLimit,
                cancellationToken,
                enforceQualityThreshold: true);
        }

            // セマンティックな section/article が一部だけ存在するページでは、
            // その外側にある本文（レース概要など）が欠落し得る。候補が取れていても
            // トップレベルの構造ブロックを補完し、既存セクションと重なるものは除外する。
            await AddSectionsFromStructuralBlocksAsync(
                pageTitle,
                sections,
                seen,
                boundedLinkLimit,
                cancellationToken);

            if (sections.Count == sectionCountAfterHeader)
            {
                await AddBodyFallbackSectionAsync(
                    pageTitle,
                    sections,
                    boundedLinkLimit,
                    cancellationToken);
            }

        await AddSpecialLayoutSectionAsync(
            selector: FooterSectionSelector,
            sections,
            seen,
            boundedLinkLimit,
            cancellationToken,
            fallbackTitle: "Footer");

        if (sections.Count > 0 && sections.All(section => section.Tables.Count == 0))
        {
            await AttachGlobalTablesAsync(sections, cancellationToken);
        }

        sections = MergeCompactSections(sections);

        if (sections.Count > 0)
        {
            return sections;
        }

        var fallbackText = TrimForSnapshot(NormalizeText(await ReadPageTextAsync()));
        var boundedFallbackLinkLimit = BoundLimit(linkLimit, MaxLinksPerSection);
        var fallbackHeadingsTask = ExtractHeadingsAsync(cancellationToken);
        var fallbackLinksTask = ExtractLinksAsync(boundedFallbackLinkLimit, cancellationToken);
        var fallbackActionsTask = ExtractActionsAsync(cancellationToken);
        var fallbackTablesTask = ExtractTablesAsync(cancellationToken);
        var fallbackFormsTask = GetFormsAsync(cancellationToken);
        var fallbackImagesTask = ExtractImagesFromRootAsync(_page.Locator("body"), MaxImagesPerSection, cancellationToken);
        await Task.WhenAll(fallbackHeadingsTask, fallbackLinksTask, fallbackActionsTask, fallbackTablesTask, fallbackFormsTask, fallbackImagesTask);

        var fallbackHeadings = (await fallbackHeadingsTask).ToList();
        var fallbackTitle = ResolveSectionTitle(fallbackHeadings, 0);

        sections.Add(new PageSectionSnapshot(
            fallbackTitle,
            fallbackText,
            links: (await fallbackLinksTask).ToList(),
            actions: (await fallbackActionsTask).Take(MaxActionsPerSection).ToList(),
            tables: (await fallbackTablesTask).Take(MaxTablesPerSection).ToList(),
            forms: (await fallbackFormsTask).Take(MaxFormsPerSection).ToList(),
            images: await fallbackImagesTask,
            headings: fallbackHeadings));
        return sections;
    }

    private async Task AddBodyFallbackSectionAsync(
        string pageTitle,
        List<PageSectionSnapshot> sections,
        int boundedLinkLimit,
        CancellationToken cancellationToken)
    {
        var root = await FindBodyFallbackRootAsync(cancellationToken);
        if (root is null)
        {
            return;
        }

        var mainText = await TryReadLimitedInnerTextAsync(root, MaxSectionTextLength);
        var headingsTask = ExtractHeadingsFromRootAsync(root, 12, cancellationToken);
        var linksTask = ExtractLinksFromRootAsync(root, boundedLinkLimit, cancellationToken);
        var actionsTask = ExtractActionsFromRootAsync(root, MaxActionsPerSection, cancellationToken);
        var tablesTask = ExtractTablesFromRootAsync(root, MaxTablesPerSection, cancellationToken);
        var formsTask = ExtractFormsFromRootAsync(root, MaxFormsPerSection, cancellationToken);
        var imagesTask = ExtractImagesFromRootAsync(root, MaxImagesPerSection, cancellationToken);
        await Task.WhenAll(linksTask, actionsTask, tablesTask, formsTask, imagesTask, headingsTask);

        var headings = (await headingsTask).ToList();
        var links = (await linksTask).ToList();
        var actions = (await actionsTask).ToList();
        var tables = (await tablesTask).ToList();
        var forms = (await formsTask).ToList();
        var images = (await imagesTask).ToList();

        if (string.IsNullOrWhiteSpace(mainText)
            && headings.Count == 0
            && links.Count == 0
            && actions.Count == 0
            && tables.Count == 0
            && forms.Count == 0
            && images.Count == 0)
        {
            return;
        }

        var title = headings.Count > 0
            ? headings[0]
            : string.IsNullOrWhiteSpace(pageTitle)
                ? "Section 1"
                : pageTitle;

        sections.Add(new PageSectionSnapshot(
            title,
            mainText,
            links,
            actions,
            tables,
            headings,
            forms,
            images));
    }

    private async Task AddSectionsFromStructuralBlocksAsync(
        string pageTitle,
        List<PageSectionSnapshot> sections,
        HashSet<string> seen,
        int boundedLinkLimit,
        CancellationToken cancellationToken)
    {
        var container = await FindStructuralSectionContainerAsync(cancellationToken);
        if (container is null)
        {
            return;
        }

        var blocks = container.Locator(":scope > *");
        var blockCount = await blocks.CountAsync();
        var structuralIndex = 0;

        for (var index = 0; index < blockCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sections.Count >= MaxSnapshotSectionCount)
            {
                return;
            }

            var block = blocks.Nth(index);
            if (!await IsElementRenderedAsync(block)
                || await IsLayoutHeaderOrFooterAsync(block))
            {
                continue;
            }

            var mainText = await TryReadLimitedInnerTextAsync(block, MaxSectionTextLength);
            if (IsTextCoveredByExistingSections(mainText, sections))
            {
                continue;
            }

            var headingsTask = ExtractHeadingsFromRootAsync(block, 12, cancellationToken);
            var linksTask = ExtractLinksFromRootAsync(block, boundedLinkLimit, cancellationToken);
            var actionsTask = ExtractActionsFromRootAsync(block, MaxActionsPerSection, cancellationToken);
            var tablesTask = ExtractTablesFromRootAsync(block, MaxTablesPerSection, cancellationToken);
            var formsTask = ExtractFormsFromRootAsync(block, MaxFormsPerSection, cancellationToken);
            var imagesTask = ExtractImagesFromRootAsync(block, MaxImagesPerSection, cancellationToken);
            await Task.WhenAll(headingsTask, linksTask, actionsTask, tablesTask, formsTask, imagesTask);

            var headings = (await headingsTask).ToList();
            var links = (await linksTask).ToList();
            var actions = (await actionsTask).ToList();
            var tables = (await tablesTask).ToList();
            var forms = (await formsTask).ToList();
            var images = (await imagesTask).ToList();

            if (string.IsNullOrWhiteSpace(mainText)
                && headings.Count == 0
                && links.Count == 0
                && actions.Count == 0
                && tables.Count == 0
                && forms.Count == 0
                && images.Count == 0)
            {
                continue;
            }

            var title = ResolveStructuralSectionTitle(pageTitle, headings, structuralIndex, links.Count, mainText.Length);
            var dedupeKey = BuildSectionDedupeKey(title, mainText);
            if (!seen.Add(dedupeKey))
            {
                continue;
            }

            sections.Add(new PageSectionSnapshot(
                title,
                mainText,
                links,
                actions,
                tables,
                headings,
                forms,
                images));

            structuralIndex++;
        }
    }

    internal static bool IsTextCoveredByExistingSections(
        string? candidateText,
        IReadOnlyList<PageSectionSnapshot> sections)
    {
        var candidate = NormalizeText(candidateText);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return true;
        }

        return sections.Any(section =>
        {
            var existing = NormalizeText(section.MainText);
            return !string.IsNullOrWhiteSpace(existing)
                && existing.Contains(candidate, StringComparison.Ordinal);
        });
    }

    private async Task<ILocator?> FindStructuralSectionContainerAsync(CancellationToken cancellationToken)
    {
        var candidates = _page.Locator("#wrapper, #container, #content, #contents, main, [role='main'], article, body");
        var count = await candidates.CountAsync();

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = candidates.Nth(index);
            if (!await IsElementRenderedAsync(candidate))
            {
                continue;
            }

            var childBlocks = candidate.Locator(":scope > *");
            if (await childBlocks.CountAsync() >= 2)
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<bool> IsLayoutHeaderOrFooterAsync(ILocator block)
        => await block.EvaluateAsync<bool>(
            """
            (node) => {
                if (!node || node.nodeType !== Node.ELEMENT_NODE) {
                    return false;
                }

                return node.matches("header, [role='banner'], #header, [id$='-header' i], [id*='_header' i], .header, [class*='header'], footer, [role='contentinfo'], #footer, [id$='-footer' i], [id*='_footer' i], .footer, [class*='footer']");
            }
            """);

    private static string ResolveStructuralSectionTitle(
        string pageTitle,
        IReadOnlyList<string> headings,
        int structuralIndex,
        int linkCount,
        int textLength)
    {
        if (headings.Count > 0)
        {
            return headings[0];
        }

        if (linkCount >= 8 && textLength <= 1200)
        {
            return "Related Links";
        }

        if (structuralIndex == 0 && !string.IsNullOrWhiteSpace(pageTitle))
        {
            return pageTitle;
        }

        return $"Section {structuralIndex + 1}";
    }

    private async Task<ILocator?> FindBodyFallbackRootAsync(CancellationToken cancellationToken)
    {
        var candidates = _page.Locator("main, [role='main'], article, body");
        var count = await candidates.CountAsync();

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = candidates.Nth(index);
            if (await IsElementRenderedAsync(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task AttachGlobalTablesAsync(List<PageSectionSnapshot> sections, CancellationToken cancellationToken)
    {
        var tables = await ExtractTablesAsync(cancellationToken);
        if (tables.Count == 0)
        {
            return;
        }

        var targetSection = sections
            .OrderByDescending(section => section.MainText.Length)
            .ThenByDescending(section => section.Headings.Count)
            .FirstOrDefault();

        if (targetSection is null)
        {
            return;
        }

        targetSection.Tables.AddRange(tables.Take(MaxTablesPerSection));
    }

    private async Task AddSpecialLayoutSectionAsync(
        string selector,
        List<PageSectionSnapshot> sections,
        HashSet<string> seen,
        int boundedLinkLimit,
        CancellationToken cancellationToken,
        string? fallbackTitle = null)
    {
        var nodes = _page.Locator(selector);
        var count = await nodes.CountAsync();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sections.Count >= MaxSnapshotSectionCount)
            {
                return;
            }

            var node = nodes.Nth(index);
            if (!await IsElementRenderedAsync(node))
            {
                continue;
            }

            var mainText = await TryReadLimitedInnerTextAsync(node, MaxSectionTextLength);
            if (string.IsNullOrWhiteSpace(mainText) || mainText.Length < MinSectionTextLength)
            {
                continue;
            }

            var headingsTask = ExtractHeadingsFromRootAsync(node, 12, cancellationToken);
            var headings = await headingsTask;
            var title = headings.Count > 0
                ? headings[0]
                : !string.IsNullOrWhiteSpace(fallbackTitle)
                    ? fallbackTitle
                    : ResolveSectionTitle(headings, index);

            var dedupeKey = BuildSectionDedupeKey(title, mainText);
            if (!seen.Add(dedupeKey))
            {
                continue;
            }

            var linksTask = ExtractLinksFromRootAsync(node, boundedLinkLimit, cancellationToken);
            var actionsTask = ExtractActionsFromRootAsync(node, MaxActionsPerSection, cancellationToken);
            var tablesTask = ExtractTablesFromRootAsync(node, MaxTablesPerSection, cancellationToken);
            var formsTask = ExtractFormsFromRootAsync(node, MaxFormsPerSection, cancellationToken);
            var imagesTask = ExtractImagesFromRootAsync(node, MaxImagesPerSection, cancellationToken);
            await Task.WhenAll(linksTask, actionsTask, tablesTask, formsTask, imagesTask);

            sections.Add(new PageSectionSnapshot(
                title,
                mainText,
                links: await linksTask,
                actions: await actionsTask,
                tables: await tablesTask,
                forms: await formsTask,
                images: await imagesTask,
                headings: headings));

            return;
        }
    }

    private async Task AddSectionsFromCandidatesAsync(
        string selector,
        List<PageSectionSnapshot> sections,
        HashSet<string> seen,
        int boundedLinkLimit,
        CancellationToken cancellationToken,
        bool enforceQualityThreshold)
    {
        var candidates = _page.Locator(selector);
        var candidateCount = await candidates.CountAsync();

        for (var index = 0; index < candidateCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sections.Count >= MaxSnapshotSectionCount)
            {
                return;
            }

            var candidate = candidates.Nth(index);
            if (await HasMatchingSectionAncestorAsync(candidate, selector))
            {
                continue;
            }

            if (!await IsElementRenderedAsync(candidate))
            {
                continue;
            }

            var mainText = await TryReadLimitedInnerTextAsync(candidate, MaxSectionTextLength);
            if (string.IsNullOrWhiteSpace(mainText) || mainText.Length < MinSectionTextLength)
            {
                continue;
            }

            var headingsTask = ExtractHeadingsFromRootAsync(candidate, 12, cancellationToken);
            var headings = await headingsTask;
            var title = ResolveSectionTitle(headings, index);
            var dedupeKey = BuildSectionDedupeKey(title, mainText);
            if (!seen.Add(dedupeKey))
            {
                continue;
            }

            var linksTask = ExtractLinksFromRootAsync(candidate, boundedLinkLimit, cancellationToken);
            var actionsTask = ExtractActionsFromRootAsync(candidate, MaxActionsPerSection, cancellationToken);
            var tablesTask = ExtractTablesFromRootAsync(candidate, MaxTablesPerSection, cancellationToken);
            var formsTask = ExtractFormsFromRootAsync(candidate, MaxFormsPerSection, cancellationToken);
            var imagesTask = ExtractImagesFromRootAsync(candidate, MaxImagesPerSection, cancellationToken);
            await Task.WhenAll(linksTask, actionsTask, tablesTask, formsTask, imagesTask);

            var links = await linksTask;
            var actions = await actionsTask;
            var tables = await tablesTask;
            var forms = await formsTask;
            var images = await imagesTask;
            if (enforceQualityThreshold && ComputeSectionQualityScore(mainText.Length, links.Count, actions.Count, tables.Count, forms.Count, images.Count) < 2)
            {
                continue;
            }

            sections.Add(new PageSectionSnapshot(title, mainText, links, actions, tables, headings, forms, images));
        }
    }

    private static async Task<bool> HasMatchingSectionAncestorAsync(ILocator candidate, string selector)
        => await candidate.EvaluateAsync<bool>(
            """
            (node, selector) => {
                if (!node || !node.parentElement) {
                    return false;
                }

                return node.parentElement.closest(selector) !== null;
            }
            """,
            selector);

    private static string BuildSectionDedupeKey(string title, string mainText)
        => $"{title}|{mainText.AsSpan(0, Math.Min(mainText.Length, 160)).ToString()}|{mainText.Length}";

    private static int ComputeSectionQualityScore(
        int textLength,
        int linkCount,
        int actionCount,
        int tableCount,
        int formCount,
        int imageCount)
    {
        var score = 0;
        if (textLength >= 80)
        {
            score += 1;
        }

        if (textLength >= 220)
        {
            score += 1;
        }

        if (linkCount >= 2)
        {
            score += 1;
        }

        if (actionCount > 0 || tableCount > 0)
        {
            score += 1;
        }

        if (formCount > 0 || imageCount >= 2)
        {
            score += 1;
        }

        return score;
    }

    private static List<PageSectionSnapshot> MergeCompactSections(List<PageSectionSnapshot> source)
    {
        if (source.Count <= 1)
        {
            return source;
        }

        var merged = new List<PageSectionSnapshot>(source.Count);
        var compactBuffer = new List<PageSectionSnapshot>(MaxMergedCompactSections);

        for (var index = 0; index < source.Count; index++)
        {
            var section = source[index];
            if (IsCompactSection(section))
            {
                compactBuffer.Add(section);
                if (compactBuffer.Count >= MaxMergedCompactSections)
                {
                    merged.Add(MergeCompactBuffer(compactBuffer));
                    compactBuffer.Clear();
                }

                continue;
            }

            if (compactBuffer.Count > 0)
            {
                merged.Add(MergeCompactBuffer(compactBuffer));
                compactBuffer.Clear();
            }

            merged.Add(section);
        }

        if (compactBuffer.Count > 0)
        {
            merged.Add(MergeCompactBuffer(compactBuffer));
        }

        return merged.Take(MaxSnapshotSectionCount).ToList();
    }

    private static bool IsCompactSection(PageSectionSnapshot section)
        => section.MainText.Length <= MergeCompactSectionTextThreshold
           && section.Tables.Count == 0
           && section.Links.Count <= 4
           && section.Actions.Count <= 2
           && section.Forms.Count == 0
           && section.Images.Count <= 2;

    private static PageSectionSnapshot MergeCompactBuffer(List<PageSectionSnapshot> compactBuffer)
    {
        if (compactBuffer.Count == 1)
        {
            return compactBuffer[0];
        }

        var title = compactBuffer[0].Title;
        var headings = compactBuffer.SelectMany(x => x.Headings)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        title = ResolveSectionTitle(headings, 0);
        var mainText = string.Join("\n", compactBuffer.Select(x => x.MainText));
        var links = compactBuffer.SelectMany(x => x.Links)
            .DistinctBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .Take(MaxLinksPerSection)
            .ToList();
        var actions = compactBuffer.SelectMany(x => x.Actions)
            .DistinctBy(x => (x.Kind, x.Text))
            .Take(MaxActionsPerSection)
            .ToList();
        var tables = compactBuffer.SelectMany(x => x.Tables)
            .Take(MaxTablesPerSection)
            .ToList();
        var forms = compactBuffer.SelectMany(x => x.Forms)
            .DistinctBy(x => $"{x.Title}|{x.Action}|{x.Method}", StringComparer.OrdinalIgnoreCase)
            .Take(MaxFormsPerSection)
            .ToList();
        var images = compactBuffer.SelectMany(x => x.Images)
            .DistinctBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .Take(MaxImagesPerSection)
            .ToList();

        return new PageSectionSnapshot(
            title: title,
            mainText: TrimForSnapshot(mainText, MaxSectionTextLength),
            links: links,
            actions: actions,
            tables: tables,
            headings: headings,
            forms: forms,
            images: images);
    }

    private async Task<string> TryReadLimitedInnerTextAsync(ILocator root, int maxLength)
    {
        try
        {
            var text = NormalizeText(await root.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 3000 }));
            return TrimForSnapshot(text, maxLength);
        }
        catch (PlaywrightException)
        {
            return string.Empty;
        }
        catch (TimeoutException)
        {
            return string.Empty;
        }
    }

    private static string ResolveSectionTitle(IReadOnlyList<string> headings, int index)
    {
        if (headings.Count > 0)
        {
            return headings[0];
        }

        return $"Section {index + 1}";
    }

    private async Task<List<string>> ExtractHeadingsFromRootAsync(
        ILocator root,
        int limit,
        CancellationToken cancellationToken)
    {
        var headings = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var locator = root.Locator("h1, h2, h3, h4, h5, h6");
        var count = await locator.CountAsync();
        var boundedLimit = BoundLimit(limit, 20);

        for (var index = 0; index < count && headings.Count < boundedLimit; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await GetLocatorTextAsync(locator.Nth(index));
            if (string.IsNullOrWhiteSpace(text) || !seen.Add(text))
            {
                continue;
            }

            headings.Add(text);
        }

        return headings;
    }

    private async Task<List<PageLinkSnapshot>> ExtractLinksFromRootAsync(
        ILocator root,
        int limit,
        CancellationToken cancellationToken)
    {
        var links = new List<PageLinkSnapshot>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anchors = root.Locator("a[href]");
        var anchorCount = await anchors.CountAsync();

        for (var index = 0; index < anchorCount && links.Count < limit; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var anchor = anchors.Nth(index);
            if (!await IsElementRenderedAsync(anchor))
            {
                continue;
            }

            var link = await CreateLinkAsync(anchor);
            if (link is null || !seenUrls.Add(link.Url))
            {
                continue;
            }

            links.Add(link);
        }

        return links.Take(limit).ToList();
    }

    private async Task<List<PageActionSnapshot>> ExtractActionsFromRootAsync(
        ILocator root,
        int maxActions,
        CancellationToken cancellationToken)
    {
        var actions = new List<PageActionSnapshot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var actionSelectors = new (string Selector, string Kind)[]
        {
            ("button", "button"),
            ("[role='button']", "button"),
            ("[role='tab']", "tab"),
            ("summary", "summary"),
            ("input[type='button'], input[type='submit']", "input"),
            ("a[title*='ドメインで検索'], a[aria-label*='ドメインで検索']", "link-action")
        };

        foreach (var (selector, kind) in actionSelectors)
        {
            var locator = root.Locator(selector);
            var count = await locator.CountAsync();
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = locator.Nth(index);
                if (!await IsElementRenderedAsync(item))
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
                if (actions.Count >= maxActions)
                {
                    return actions;
                }
            }
        }

        var anchorLocator = root.Locator("a[href]");
        var anchorCount = await anchorLocator.CountAsync();
        for (var index = 0; index < anchorCount && actions.Count < maxActions; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var anchor = anchorLocator.Nth(index);
            if (!await IsElementRenderedAsync(anchor))
            {
                continue;
            }

            var pseudoAction = await TryCreatePseudoActionFromAnchorAsync(anchor);
            if (pseudoAction is null)
            {
                continue;
            }

            var key = $"{pseudoAction.Kind}:{pseudoAction.Text}";
            if (!seen.Add(key))
            {
                continue;
            }

            actions.Add(pseudoAction);
        }

        return actions;
    }

    private async Task<List<PageTableSnapshot>> ExtractTablesFromRootAsync(
        ILocator root,
        int maxTables,
        CancellationToken cancellationToken)
    {
        var tables = new List<PageTableSnapshot>();
        var tableLocator = root.Locator("table");
        var tableCount = await tableLocator.CountAsync();

        for (var tableIndex = 0; tableIndex < tableCount && tables.Count < maxTables; tableIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var table = tableLocator.Nth(tableIndex);
            if (!await IsElementRenderedAsync(table))
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

    private async Task<List<PageFormSnapshot>> ExtractFormsFromRootAsync(
        ILocator root,
        int maxForms,
        CancellationToken cancellationToken)
    {
        var forms = new List<PageFormSnapshot>();
        var formLocator = root.Locator("form");
        var formCount = await formLocator.CountAsync();

        for (var formIndex = 0; formIndex < formCount && forms.Count < maxForms; formIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var form = formLocator.Nth(formIndex);
            if (!await IsElementRenderedAsync(form))
            {
                continue;
            }

            var title = await ExtractFormTitleAsync(form, formIndex);
            var action = await form.GetAttributeAsync("action") ?? string.Empty;
            var method = (await form.GetAttributeAsync("method") ?? "GET").ToUpperInvariant();
            var fields = await ExtractFormFieldsAsync(form, cancellationToken);

            forms.Add(new PageFormSnapshot(title, action, method, fields));
        }

        return forms;
    }

    private async Task<List<PageImageSnapshot>> ExtractImagesFromRootAsync(
        ILocator root,
        int maxImages,
        CancellationToken cancellationToken)
    {
        var images = new List<PageImageSnapshot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var imageLocator = root.Locator("img");
        var imageCount = await imageLocator.CountAsync();

        for (var index = 0; index < imageCount && images.Count < maxImages; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var image = imageLocator.Nth(index);
            if (!await IsElementRenderedAsync(image))
            {
                continue;
            }

            var src = await image.GetAttributeAsync("src") ?? string.Empty;
            var resolvedUrl = ResolveImageUrl(src, CurrentUrl);
            var alt = NormalizeText(await image.GetAttributeAsync("alt"));
            var title = NormalizeText(await image.GetAttributeAsync("title"));

            if (string.IsNullOrWhiteSpace(resolvedUrl) && string.IsNullOrWhiteSpace(alt) && string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var dedupeKey = !string.IsNullOrWhiteSpace(resolvedUrl)
                ? resolvedUrl
                : $"{alt}|{title}";
            if (!seen.Add(dedupeKey))
            {
                continue;
            }

            images.Add(new PageImageSnapshot(
                Url: resolvedUrl,
                Alt: alt,
                Title: title,
                Region: await DetermineRegionAsync(image)));
        }

        return images;
    }

    private static string ResolveImageUrl(string? src, string? currentUrl)
    {
        if (string.IsNullOrWhiteSpace(src))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(src, UriKind.Absolute, out var absolute) &&
            absolute.Scheme is "http" or "https")
        {
            return absolute.AbsoluteUri;
        }

        if (!string.IsNullOrWhiteSpace(currentUrl)
            && Uri.TryCreate(currentUrl, UriKind.Absolute, out var current)
            && Uri.TryCreate(current, src, out var resolved)
            && resolved.Scheme is "http" or "https")
        {
            return resolved.AbsoluteUri;
        }

        return string.Empty;
    }

    private static int BoundLimit(int requestedLimit, int hardLimit)
    {
        if (requestedLimit <= 0)
        {
            return hardLimit;
        }

        return Math.Min(requestedLimit, hardLimit);
    }

    private static string TrimForSnapshot(string value, int maxLength = MaxSectionTextLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
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
            ("input[type='button'], input[type='submit']", "input"),
            ("a[title*='ドメインで検索'], a[aria-label*='ドメインで検索']", "link-action")
        };

        foreach (var (selector, kind) in actionSelectors)
        {
            var locator = _page.Locator(selector);
            var count = await locator.CountAsync();
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = locator.Nth(index);
                if (!await IsElementRenderedAsync(item))
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

        var anchorLocator = _page.Locator("a[href]");
        var anchorCount = await anchorLocator.CountAsync();
        for (var index = 0; index < anchorCount && actions.Count < 50; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var anchor = anchorLocator.Nth(index);
            if (!await IsElementRenderedAsync(anchor))
            {
                continue;
            }

            var pseudoAction = await TryCreatePseudoActionFromAnchorAsync(anchor);
            if (pseudoAction is null)
            {
                continue;
            }

            var key = $"{pseudoAction.Kind}:{pseudoAction.Text}";
            if (!seen.Add(key))
            {
                continue;
            }

            actions.Add(pseudoAction);
        }

        return actions;
    }

    private async Task<IReadOnlyList<PageTableSnapshot>> ExtractTablesAsync(CancellationToken cancellationToken)
    {
        var tables = new List<PageTableSnapshot>();
        var tableLocator = _page.Locator("table");
        var tableCount = await tableLocator.CountAsync();

        for (var tableIndex = 0; tableIndex < tableCount && tables.Count < MaxSnapshotTableCount; tableIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var table = tableLocator.Nth(tableIndex);
            if (!await IsElementRenderedAsync(table))
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

        for (var rowIndex = 0; rowIndex < rowCount && rows.Count < MaxSnapshotRowsPerTable; rowIndex++)
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
            if (!await IsElementRenderedAsync(candidate))
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
            if (!await IsElementRenderedAsync(candidate))
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
            if (!await IsElementRenderedAsync(marker))
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
                if (!await IsElementRenderedAsync(candidate))
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

    private async Task<IReadOnlyList<PageLinkSnapshot>> ExtractLinksAsync(int limit, CancellationToken cancellationToken)
    {
        var links = new List<PageLinkSnapshot>();
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
        if (await modal.CountAsync() == 0 || !await IsElementRenderedAsync(modal))
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
        List<PageLinkSnapshot> links,
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

    private async Task<PageLinkSnapshot?> CreateLinkAsync(ILocator anchor)
    {
        var url = await anchor.GetAttributeAsync("href") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var title = await GetLocatorTextAsync(anchor);
        var ariaLabel = NormalizeText(await anchor.GetAttributeAsync("aria-label"));
        var titleAttribute = NormalizeText(await anchor.GetAttributeAsync("title"));
        if (IsDomainSearchPseudoActionAnchor(title, ariaLabel, titleAttribute, url))
        {
            return null;
        }

        var region = await DetermineRegionAsync(anchor);
        return new PageLinkSnapshot(
            url,
            string.IsNullOrWhiteSpace(title) ? url : title,
            region);
    }

    private static bool IsDomainSearchPseudoActionAnchor(
        string? linkText,
        string? ariaLabel,
        string? titleAttribute,
        string? href)
    {
        static bool ContainsDomainSearchPhrase(string? value)
            => !string.IsNullOrWhiteSpace(value)
               && value.Contains("ドメインで検索", StringComparison.Ordinal);

        if (ContainsDomainSearchPhrase(linkText)
            || ContainsDomainSearchPhrase(ariaLabel)
            || ContainsDomainSearchPhrase(titleAttribute))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        var normalizedHref = href.Trim();
        return normalizedHref.StartsWith("/?q=", StringComparison.OrdinalIgnoreCase)
            && normalizedHref.Contains("site:", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<PageActionSnapshot?> TryCreatePseudoActionFromAnchorAsync(ILocator anchor)
    {
        var href = await anchor.GetAttributeAsync("href") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var text = await GetLocatorTextAsync(anchor);
        var ariaLabel = NormalizeText(await anchor.GetAttributeAsync("aria-label"));
        var titleAttribute = NormalizeText(await anchor.GetAttributeAsync("title"));
        if (!IsDomainSearchPseudoActionAnchor(text, ariaLabel, titleAttribute, href))
        {
            return null;
        }

        var resolvedText = !string.IsNullOrWhiteSpace(text)
            ? text
            : !string.IsNullOrWhiteSpace(ariaLabel)
                ? ariaLabel
                : !string.IsNullOrWhiteSpace(titleAttribute)
                    ? titleAttribute
                    : href;

        return new PageActionSnapshot(resolvedText, "link-action");
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
