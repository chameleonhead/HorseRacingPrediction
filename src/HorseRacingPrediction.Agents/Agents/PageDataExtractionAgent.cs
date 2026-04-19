using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// Web ページの生テキストを受け取り、LLM を使って
/// ヘッダー・フッター・ナビゲーション等のノイズを除去した
/// 整理済みテキスト＋リンク集を出力するページ整形エージェント。
/// <para>
/// PlaywrightTools のツールが返すページ本文を、
/// WebBrowserAgent が判断しやすいクリーンなドキュメントに変換する。
/// 要約しすぎず、本文の情報をできるだけ保持する。
/// </para>
/// </summary>
public sealed class PageDataExtractionAgent
{
    private const int MaxLoggedTextLength = 8_000;
    private const int MaxInputLength = 12_000;
    private const int MaxPromptLinks = 20;
    private const int MaxSearchResultLinks = 12;

    private const string AnalysisPrompt = """
        あなたはWebページの生テキストを読みやすく整形し、必要なら詳細表示への1回だけの追加入力が必要かを判定する専門エージェントです。
        必ず JSON オブジェクトのみを返してください。コードブロックや説明文は不要です。

        ## 目的
        - ヘッダー、フッター、サイドバー、ナビゲーション、Cookie通知、広告、ログイン導線など本文に無関係な UI テキストを除去する
        - 本文コンテンツはできる限り元の記述を保つが、重複や定型文は圧縮し、出力は必要十分な長さに抑える
        - 現在ページだけでは詳細本文に到達しておらず、同一ページ内の「詳細を表示」「もっと見る」等の 1 回だけのクリックが必要そうな場合に限って、そのクリック文言を提案する

        ## click 判定ルール
        - shouldFollowDetailLink は、現在のページが一覧・概要・折りたたみ状態で、詳細本文がまだ見えていないと判断できる場合だけ true にする
        - detailLinkText には、与えられたリンク一覧または生テキスト中に実際に現れる文言だけをそのまま入れる
        - クリック不要なら shouldFollowDetailLink=false, detailLinkText=null にする
        - 別ページへの一般リンク遷移を積極的に提案しない。あくまで詳細表示のための 1 回の追加クリックだけを扱う

        ## JSON 形式
        {
            "contentMarkdown": "整形済み本文 Markdown",
            "shouldFollowDetailLink": false,
            "detailLinkText": null
        }
        """;

    private const string SystemPrompt = """
        あなたはWebページの生テキストを読みやすく整形する専門エージェントです。
        ブラウザから取得したページの生テキストを受け取り、整理されたドキュメントとして出力します。

        ## 整形ルール
        - ヘッダー、フッター、サイドバー、ナビゲーションメニュー、Cookie通知、広告、ログインボタン等のUI要素テキストは省く
        - 本文コンテンツ（記事、データ表、リスト、説明文等）は保持するが、同じ意味の繰り返しや定型的な導線文はまとめてよい
        - 出力は短めを優先する。ただし、日付、場所、数値、表、見出し、条件、手順、注意書きなど判断に重要な情報は落とさない
        - 表形式のデータ（レース結果、出馬表等）はMarkdownの表として整形する
        - 箇条書きやリストはそのまま維持する
        - ヘッダーやフッターにあるリンク群がページ利用上重要な場合は、本文とは分けて分かりやすいリンク集として残してよい
        - ページ内で見つかったコンテンツに関連するリンクがあれば、末尾に「## リンク」セクションとしてまとめる
        - URL を出力する場合は、入力で与えられた現在ページ URL またはリンク一覧に含まれる URL だけをそのまま使う。URL を推測・生成・書き換えしない
        - リンクは `- [タイトル](URL)` の Markdown 形式で出力する
        - 検索エンジン、メール、ログイン系のリンクは除外する

        ## 検索結果ページの場合
        URL が bing.com や google.com の検索結果ページの場合は、以下のルールを適用する:
        - 通常の検索結果だけを優先し、関連検索、広告、「他の人はこちらも検索」、ログイン、設定、画像、動画、地図、翻訳などの周辺UIは省く
        - 重要そうな候補を優先して短いリンク集にまとめる
        - ただし候補が少ない場合は削りすぎず、主要候補は漏らさない
        - 出力形式は Markdown のリンク一覧を基本とし、必要なら最後に補足を 1 行だけ付ける

        ## 出力形式
        Markdown形式で出力してください。余計な前置きや説明は不要です。
        """;

    private readonly IChatClient _chatClient;
    private readonly ILogger<PageDataExtractionAgent> _logger;

    public PageDataExtractionAgent(
        IChatClient chatClient,
        ILogger<PageDataExtractionAgent>? logger = null)
    {
        _chatClient = chatClient;
        _logger = logger ?? NullLogger<PageDataExtractionAgent>.Instance;
    }

    /// <summary>
    /// ページの生テキストを LLM で整形し、クリーンなドキュメントを返す。
    /// </summary>
    /// <param name="rawPageText">ブラウザから取得したページの innerText</param>
    /// <param name="pageUrl">ページの URL（コンテキスト情報として使用）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>整形済みの Markdown テキスト</returns>
    public async Task<string> FormatPageContentAsync(
        string rawPageText,
        string pageUrl,
        IReadOnlyList<SearchResultLink>? pageLinks = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPageText))
        {
            return string.Empty;
        }

        if (IsSearchResultsPage(pageUrl) && pageLinks is { Count: > 0 })
        {
            return BuildSearchResultLinkCollection(pageLinks);
        }

        var truncatedText = rawPageText.Length > MaxInputLength
            ? rawPageText[..MaxInputLength]
            : rawPageText;

        var linksText = pageLinks is { Count: > 0 }
            ? BuildLinksPromptText(pageLinks)
            : "(リンクなし)";

        var userMessage = $"""
            以下のWebページの内容を整形してください。

            URL: {pageUrl}

            --- 使用可能なリンク一覧 ---
            {linksText}

            --- ページ生テキスト ---
            {truncatedText}
            """;

        var messages = new ChatMessage[]
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userMessage),
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            return string.IsNullOrWhiteSpace(response.Text)
                ? rawPageText
                : SanitizeUrls(response.Text, pageUrl, pageLinks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PageDataExtractionAgent FormatPageContent failed. Url={PageUrl}", pageUrl);
            // LLM 呼び出しに失敗した場合は生テキストをそのまま返す
            return rawPageText;
        }
    }

    /// <summary>
    /// ページの構造化スナップショットを LLM で整形し、クリーンなドキュメントを返す。
    /// </summary>
    public Task<string> FormatPageContentAsync(
        PageSnapshot snapshot,
        CancellationToken cancellationToken = default)
        => FormatPageContentInternalAsync(snapshot.MainText, snapshot.Url, snapshot.Links, snapshot, cancellationToken);

    /// <summary>
    /// ページ本文の整形と、詳細表示のための追加クリック要否を同時に判定する。
    /// </summary>
    public async Task<PageExtractionResult> AnalyzePageAsync(
        string rawPageText,
        string pageUrl,
        string? objective,
        IReadOnlyList<SearchResultLink> pageLinks,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPageText))
        {
            return new PageExtractionResult(string.Empty, false, null);
        }

        var truncatedText = rawPageText.Length > MaxInputLength
            ? rawPageText[..MaxInputLength]
            : rawPageText;

        var linksText = pageLinks.Count == 0
            ? "(リンクなし)"
            : BuildLinksPromptText(pageLinks);

        var userMessage = $"""
            以下のWebページを分析してください。

            URL: {pageUrl}
            調査目的: {objective ?? "(指定なし)"}

            --- ページ内リンク一覧 ---
            {linksText}

            --- ページ生テキスト ---
            {truncatedText}
            """;

        var messages = new ChatMessage[]
        {
            new(ChatRole.System, AnalysisPrompt),
            new(ChatRole.User, userMessage),
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            var responseText = response.Text;
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return new PageExtractionResult(rawPageText, false, null);
            }

            var json = ExtractJsonObject(responseText);
            var parsed = JsonSerializer.Deserialize<PageExtractionResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            var contentMarkdown = string.IsNullOrWhiteSpace(parsed?.ContentMarkdown)
                ? rawPageText
                : SanitizeUrls(parsed.ContentMarkdown, pageUrl, pageLinks);

            var detailLinkText = NormalizeDetailLinkText(parsed?.DetailLinkText, rawPageText, pageLinks);
            var shouldFollow = parsed?.ShouldFollowDetailLink == true && !string.IsNullOrWhiteSpace(detailLinkText);

            return new PageExtractionResult(contentMarkdown, shouldFollow, detailLinkText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PageDataExtractionAgent AnalyzePage failed. Url={PageUrl}", pageUrl);
            return new PageExtractionResult(rawPageText, false, null);
        }
    }

    /// <summary>
    /// ページ構造スナップショットを使って本文整形と追加クリック要否を同時に判定する。
    /// </summary>
    public Task<PageExtractionResult> AnalyzePageAsync(
        PageSnapshot snapshot,
        string? objective,
        CancellationToken cancellationToken = default)
        => AnalyzePageInternalAsync(snapshot.MainText, snapshot.Url, objective, snapshot.Links, snapshot, cancellationToken);

    private Task<string> FormatPageContentInternalAsync(
        string rawPageText,
        string pageUrl,
        IReadOnlyList<SearchResultLink>? pageLinks,
        PageSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        return FormatPageContentCoreAsync(rawPageText, pageUrl, pageLinks, snapshot, cancellationToken);
    }

    private async Task<string> FormatPageContentCoreAsync(
        string rawPageText,
        string pageUrl,
        IReadOnlyList<SearchResultLink>? pageLinks,
        PageSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawPageText))
        {
            return string.Empty;
        }

        if (IsSearchResultsPage(pageUrl) && pageLinks is { Count: > 0 })
        {
            return BuildSearchResultLinkCollection(pageLinks);
        }

        var truncatedText = rawPageText.Length > MaxInputLength
            ? rawPageText[..MaxInputLength]
            : rawPageText;

        var linksText = pageLinks is { Count: > 0 }
            ? BuildLinksPromptText(pageLinks)
            : "(リンクなし)";

        var snapshotText = snapshot is null
            ? "(スナップショットなし)"
            : BuildSnapshotJson(snapshot);

        var userMessage = $"""
            以下のWebページの内容を整形してください。

            URL: {pageUrl}

            --- ページ構造スナップショット(JSON) ---
            {snapshotText}

            --- 使用可能なリンク一覧 ---
            {linksText}

            --- ページ生テキスト ---
            {truncatedText}
            """;

        var messages = new ChatMessage[]
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userMessage),
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            return string.IsNullOrWhiteSpace(response.Text)
                ? rawPageText
                : SanitizeUrls(response.Text, pageUrl, pageLinks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PageDataExtractionAgent FormatPageContent failed. Url={PageUrl}", pageUrl);
            return rawPageText;
        }
    }

    private Task<PageExtractionResult> AnalyzePageInternalAsync(
        string rawPageText,
        string pageUrl,
        string? objective,
        IReadOnlyList<SearchResultLink> pageLinks,
        PageSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        return AnalyzePageCoreAsync(rawPageText, pageUrl, objective, pageLinks, snapshot, cancellationToken);
    }

    private async Task<PageExtractionResult> AnalyzePageCoreAsync(
        string rawPageText,
        string pageUrl,
        string? objective,
        IReadOnlyList<SearchResultLink> pageLinks,
        PageSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawPageText))
        {
            return new PageExtractionResult(string.Empty, false, null);
        }

        var truncatedText = rawPageText.Length > MaxInputLength
            ? rawPageText[..MaxInputLength]
            : rawPageText;

        var linksText = pageLinks.Count == 0
            ? "(リンクなし)"
            : BuildLinksPromptText(pageLinks);

        var snapshotText = snapshot is null
            ? "(スナップショットなし)"
            : BuildSnapshotJson(snapshot);

        var userMessage = $"""
            以下のWebページを分析してください。

            URL: {pageUrl}
            調査目的: {objective ?? "(指定なし)"}

            --- ページ構造スナップショット(JSON) ---
            {snapshotText}

            --- ページ内リンク一覧 ---
            {linksText}

            --- ページ生テキスト ---
            {truncatedText}
            """;

        var messages = new ChatMessage[]
        {
            new(ChatRole.System, AnalysisPrompt),
            new(ChatRole.User, userMessage),
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            var responseText = response.Text;
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return new PageExtractionResult(rawPageText, false, null);
            }

            var json = ExtractJsonObject(responseText);
            var parsed = JsonSerializer.Deserialize<PageExtractionResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            var contentMarkdown = string.IsNullOrWhiteSpace(parsed?.ContentMarkdown)
                ? rawPageText
                : SanitizeUrls(parsed.ContentMarkdown, pageUrl, pageLinks);

            var detailLinkText = NormalizeDetailLinkText(parsed?.DetailLinkText, rawPageText, pageLinks);
            var shouldFollow = parsed?.ShouldFollowDetailLink == true && !string.IsNullOrWhiteSpace(detailLinkText);

            return new PageExtractionResult(contentMarkdown, shouldFollow, detailLinkText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PageDataExtractionAgent AnalyzePage failed. Url={PageUrl}", pageUrl);
            return new PageExtractionResult(rawPageText, false, null);
        }
    }

    private static bool IsSearchResultsPage(string pageUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.Contains("google.", StringComparison.Ordinal) || host.Contains("bing.", StringComparison.Ordinal))
        {
            return uri.AbsolutePath.Contains("search", StringComparison.OrdinalIgnoreCase) ||
                   uri.Query.Contains("q=", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string BuildSearchResultLinkCollection(IReadOnlyList<SearchResultLink> pageLinks)
    {
        var primaryLinks = SelectSearchResultLinks(pageLinks);
        var sb = new StringBuilder();
        sb.AppendLine("## 検索結果候補");
        sb.AppendLine();

        foreach (var link in primaryLinks)
        {
            if (string.IsNullOrWhiteSpace(link.Url))
            {
                continue;
            }

            var title = string.IsNullOrWhiteSpace(link.Title)
                ? link.Url
                : link.Title;

            sb.AppendLine($"- [{title}]({link.Url})");
        }

        var omittedCount = pageLinks.Count - primaryLinks.Count;
        if (omittedCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"補足: 関連性の低い候補 {omittedCount} 件は省略しました。");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildLinksPromptText(IReadOnlyList<SearchResultLink> pageLinks)
    {
        var selectedLinks = pageLinks
            .Where(link => !string.IsNullOrWhiteSpace(link.Url))
            .Take(MaxPromptLinks)
            .Select(link => $"- {link.Title} | {link.Url}")
            .ToList();

        if (selectedLinks.Count == 0)
        {
            return "(リンクなし)";
        }

        if (pageLinks.Count > selectedLinks.Count)
        {
            selectedLinks.Add($"... 他 {pageLinks.Count - selectedLinks.Count} 件");
        }

        return string.Join(Environment.NewLine, selectedLinks);
    }

    private static List<SearchResultLink> SelectSearchResultLinks(IReadOnlyList<SearchResultLink> pageLinks)
    {
        return pageLinks
            .Where(link => !string.IsNullOrWhiteSpace(link.Url))
            .Where(link => !IsSearchNoiseLink(link))
            .DistinctBy(link => link.Url)
            .OrderByDescending(GetSearchResultPriority)
            .ThenBy(link => link.Title.Length)
            .Take(MaxSearchResultLinks)
            .ToList();
    }

    private static int GetSearchResultPriority(SearchResultLink link)
    {
        var score = 0;
        var title = (link.Title ?? string.Empty).ToLowerInvariant();
        var url = (link.Url ?? string.Empty).ToLowerInvariant();

        if (link.Region == "content")
        {
            score += 20;
        }

        if (!url.Contains("google.", StringComparison.Ordinal) && !url.Contains("bing.", StringComparison.Ordinal))
        {
            score += 20;
        }

        if (title.Contains("公式", StringComparison.Ordinal) || title.Contains("jra", StringComparison.Ordinal))
        {
            score += 15;
        }

        if (title.Contains("出馬", StringComparison.Ordinal) ||
            title.Contains("レース", StringComparison.Ordinal) ||
            title.Contains("結果", StringComparison.Ordinal) ||
            title.Contains("開催", StringComparison.Ordinal) ||
            title.Contains("皐月賞", StringComparison.Ordinal))
        {
            score += 10;
        }

        return score;
    }

    private static bool IsSearchNoiseLink(SearchResultLink link)
    {
        var title = (link.Title ?? string.Empty).ToLowerInvariant();
        var url = (link.Url ?? string.Empty).ToLowerInvariant();

        if (link.Region is "header" or "footer")
        {
            return true;
        }

        return title.Contains("ログイン", StringComparison.Ordinal) ||
               title.Contains("sign in", StringComparison.Ordinal) ||
               title.Contains("設定", StringComparison.Ordinal) ||
               title.Contains("画像", StringComparison.Ordinal) ||
               title.Contains("動画", StringComparison.Ordinal) ||
               title.Contains("地図", StringComparison.Ordinal) ||
               title.Contains("翻訳", StringComparison.Ordinal) ||
               title.Contains("キャッシュ", StringComparison.Ordinal) ||
               title.Contains("広告", StringComparison.Ordinal) ||
               title.Contains("関連検索", StringComparison.Ordinal) ||
               url.Contains("accounts.google.com", StringComparison.Ordinal) ||
               url.Contains("support.google.com", StringComparison.Ordinal) ||
               url.Contains("policies.google.com", StringComparison.Ordinal);
    }

    private static string BuildSnapshotJson(PageSnapshot snapshot)
    {
        var compactSnapshot = new
        {
            snapshot.Url,
            snapshot.Title,
            snapshot.Headings,
            MainText = TruncateSnapshotText(snapshot.MainText),
            Links = snapshot.Links.Take(50).Select(link => new { link.Title, link.Url, link.Region }),
            Actions = snapshot.Actions.Take(30).Select(action => new { action.Text, action.Kind }),
            Tables = snapshot.Tables.Take(5).Select(table => new
            {
                Headers = table.Headers,
                Rows = table.Rows.Take(10)
            })
        };

        return JsonSerializer.Serialize(compactSnapshot, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    private static string TruncateSnapshotText(string text)
    {
        return text.Length <= 4_000
            ? text
            : text[..4_000];
    }

    private static string ExtractJsonObject(string responseText)
    {
        var trimmed = responseText.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var fenceEnd = trimmed.IndexOf('\n');
            var bodyStart = fenceEnd >= 0 ? fenceEnd + 1 : 3;
            var closingFence = trimmed.IndexOf("```", bodyStart, StringComparison.Ordinal);
            if (closingFence > bodyStart)
            {
                trimmed = trimmed[bodyStart..closingFence].Trim();
            }
        }

        var jsonStart = trimmed.IndexOf('{');
        if (jsonStart < 0)
        {
            return trimmed;
        }

        var depth = 0;
        var inString = false;
        var isEscaped = false;

        for (var index = jsonStart; index < trimmed.Length; index++)
        {
            var ch = trimmed[index];

            if (inString)
            {
                if (isEscaped)
                {
                    isEscaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    isEscaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
                continue;
            }

            if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return trimmed[jsonStart..(index + 1)];
                }
            }
        }

        return trimmed[jsonStart..];
    }

    private static string? NormalizeDetailLinkText(
        string? detailLinkText,
        string rawPageText,
        IReadOnlyList<SearchResultLink> pageLinks)
    {
        if (string.IsNullOrWhiteSpace(detailLinkText))
        {
            return null;
        }

        var candidate = detailLinkText.Trim();
        var matchingLink = pageLinks.FirstOrDefault(link =>
            string.Equals(link.Title, candidate, StringComparison.Ordinal));

        if (matchingLink is not null)
        {
            return matchingLink.Title;
        }

        return rawPageText.Contains(candidate, StringComparison.Ordinal)
            ? candidate
            : null;
    }

    private sealed class PageExtractionResponse
    {
        public string? ContentMarkdown { get; init; }

        public bool ShouldFollowDetailLink { get; init; }

        public string? DetailLinkText { get; init; }
    }

    private static string SanitizeUrls(
        string markdown,
        string pageUrl,
        IReadOnlyList<SearchResultLink>? pageLinks)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return markdown;
        }

        var allowedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var currentPageUri) &&
            currentPageUri.Scheme is "http" or "https")
        {
            allowedUrls.Add(currentPageUri.AbsoluteUri);
        }

        if (pageLinks is not null)
        {
            foreach (var link in pageLinks)
            {
                if (Uri.TryCreate(link.Url, UriKind.Absolute, out var linkUri) &&
                    linkUri.Scheme is "http" or "https")
                {
                    allowedUrls.Add(linkUri.AbsoluteUri);
                }
            }
        }

        var sanitized = Regex.Replace(
            markdown,
            @"\[(?<text>[^\]]+)\]\((?<url>https?://[^)\s]+)\)",
            match =>
            {
                var url = match.Groups["url"].Value;
                return allowedUrls.Contains(url)
                    ? match.Value
                    : match.Groups["text"].Value;
            },
            RegexOptions.IgnoreCase);

        sanitized = Regex.Replace(
            sanitized,
            @"(?<!\()\bhttps?://[^\s)]+",
            match => allowedUrls.Contains(match.Value) ? match.Value : "(未検証URL削除)",
            RegexOptions.IgnoreCase);

        return sanitized;
    }
}
