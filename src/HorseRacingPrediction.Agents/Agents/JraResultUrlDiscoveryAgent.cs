using System.Text.Json;
using System.Text.Json.Serialization;
using HorseRacingPrediction.Agents.Browser;
using HorseRacingPrediction.Agents.Scrapers.Jra;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// JRA 公式サイトの成績ページを閲覧し、
/// 指定週末に開催されたレースの成績 URL 一覧を返す軽量エージェント。
/// <para>
/// このエージェントは成績 URL を「発見する」だけであり、各ページの詳細を読まない。
/// 実際の成績データの抽出は <see cref="Scrapers.Jra.JraRaceResultScraper"/> が担う。
/// </para>
/// <para>
/// 使用ツール: <see cref="PlaywrightTools.BrowserReadPage"/>（成績一覧ページへの直接アクセス）
/// </para>
/// </summary>
public sealed class JraResultUrlDiscoveryAgent
{
    public const string AgentName = "JraResultUrlDiscoveryAgent";

    public const string SystemPrompt = """
        あなたはJRA公式サイトから成績（レース結果）ページのURLを収集する専門エージェントです。
        指定された日付（または当週末）に開催されたレースの成績URLを収集し、
        **JSON配列のみ**で返してください。説明文・Markdown・コードブロックは一切含めないこと。

        ## 行動方針
        1. BrowserReadPage ツールで `https://www.jra.go.jp/keiba/results/` にアクセスする
        2. 対象日付に合致する成績リンクを探す
           - URL に `CNAME=pw01skd0203_` が含まれているリンクが成績ページのURLです
           - 対象日付に合致するURLのみを選択し、他の日付は除外する
        3. 対象日のリンクが見つからない場合は、競馬場ごとの個別ページも確認する
        4. リンクが全く見つからない場合は空配列 [] を返す

        ## 出力形式（JSON 配列）
        [
          {
            "url": "https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01skd0203_20250420051101&sub=",
            "racecourse": "東京",
            "raceDate": "2025-04-20",
            "raceNumber": 11
          }
        ]

        ## 重要なルール
        - ページを 1〜3 ページ程度しか読まない（詳細ページは読まない）
        - raceDate は `YYYY-MM-DD` 形式で返す
        - raceNumber は整数で返す
        - URL は自分で生成せず、ページから取得したリンクのみを使う
        - racecourse・raceDate・raceNumber が不明な場合は null を設定する
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ChatClientAgent _innerAgent;

    public JraResultUrlDiscoveryAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// 指定した週末の開催日に対応する成績 URL 一覧を返す。
    /// </summary>
    /// <param name="raceDate">対象の開催日付</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>発見された成績 URL の一覧</returns>
    public async Task<IReadOnlyList<JraRaceResultUrl>> DiscoverUrlsAsync(
        DateOnly raceDate,
        CancellationToken cancellationToken = default)
    {
        var result = await _innerAgent.RunAsync(
            $"JRA の {raceDate:yyyy年M月d日} に開催されたレースの成績URL一覧を収集してください。",
            cancellationToken: cancellationToken);

        return ParseJsonResponse(result.Text, raceDate);
    }

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private static IReadOnlyList<JraRaceResultUrl> ParseJsonResponse(string responseText, DateOnly requestedDate)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return [];
        }

        var jsonText = ExtractJsonArray(responseText);
        if (jsonText is null)
        {
            return [];
        }

        try
        {
            var dtos = JsonSerializer.Deserialize<List<DiscoveredUrlDto>>(jsonText, JsonOptions);
            if (dtos is null)
            {
                return [];
            }

            return dtos
                .Where(dto => !string.IsNullOrWhiteSpace(dto.Url))
                .Select(dto =>
                {
                    var url = JraRaceResultUrl.ParseFromUrl(dto.Url!, dto.Racecourse);

                    // raceDate が未解析の場合はエージェントが返した値を使う
                    if (url.RaceDate is null && dto.RaceDate is not null &&
                        DateOnly.TryParse(dto.RaceDate, out var parsedDate))
                    {
                        url = url with { RaceDate = parsedDate };
                    }

                    // raceNumber が未解析の場合はエージェントが返した値を使う
                    if (url.RaceNumber is null && dto.RaceNumber is not null)
                    {
                        url = url with { RaceNumber = dto.RaceNumber };
                    }

                    return url;
                })
                .Where(url => url.RaceDate is null || url.RaceDate.Value == requestedDate)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return text[start..(end + 1)];
    }

    private sealed class DiscoveredUrlDto
    {
        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("racecourse")]
        public string? Racecourse { get; init; }

        [JsonPropertyName("raceDate")]
        public string? RaceDate { get; init; }

        [JsonPropertyName("raceNumber")]
        public int? RaceNumber { get; init; }
    }
}
