using System.Text.Json;
using System.Text.Json.Serialization;
using HorseRacingPrediction.Agents.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// JRA 公式サイトや netkeiba などから週末（土・日）に開催されるレース一覧を発見し、
/// 出走馬・騎手・調教師の情報とともに <see cref="WeekendRaceInfo"/> のリストとして返す
/// 自律型エージェント。
/// <para>
/// 木曜時点では出走馬が確定していない場合があるため、登録馬情報を可能な範囲で収集する。
/// 金曜の枠順確定後に再実行すると、より完全な出走馬一覧を取得できる。
/// </para>
/// </summary>
public sealed class WeekendRaceDiscoveryAgent
{
    internal const string AgentName = "WeekendRaceDiscoveryAgent";

    internal const string SystemPrompt = """
        あなたは週末の競馬レーススケジュールを調査する専門エージェントです。
        指定された週末（土・日）に開催されるレースの一覧を JRA 公式サイトや
        netkeiba などから収集し、**必ず** 以下の JSON 配列形式のみで返してください。
        説明文・Markdown・コードブロック以外のテキストは一切含めないこと。

        ## 出力形式（JSON 配列）
        ```json
        [
          {
            "raceName": "レース名",
            "raceDate": "YYYY-MM-DD",
            "racecourse": "競馬場名",
            "raceNumber": 11,
            "raceQuery": "YYYY年レース名 競馬場NR",
            "horseNames": ["馬名1", "馬名2"],
            "jockeyNames": ["騎手名1", "騎手名2"],
            "trainerNames": ["調教師名1", "調教師名2"]
          }
        ]
        ```

        ## 行動方針
        - `SearchAndFetch` で「JRA YYYY年MM月DD日 開催レース一覧」などを検索する
        - `FetchPageContent` で JRA 公式スケジュールページを取得する
        - `FetchRaceCard` で出馬表を取得し、馬名・騎手名・調教師名を抽出する
        - 木曜時点では出走馬が確定していない場合があるため、登録馬情報を可能な限り収集する
        - 取得できなかった項目（馬名・騎手名・調教師名）は空配列 [] を設定する
        """;

    private readonly ChatClientAgent _innerAgent;

    public WeekendRaceDiscoveryAgent(IChatClient chatClient, IList<AITool> tools)
    {
        _innerAgent = new ChatClientAgent(
            chatClient,
            name: AgentName,
            instructions: SystemPrompt,
            tools: tools);
    }

    /// <summary>
    /// 指定された週末に開催されるレースを検索し、参加馬・騎手・調教師情報とともに返す。
    /// </summary>
    /// <param name="targetWeekend">
    /// 対象週末内のいずれかの日付。土曜日以外が指定された場合は自動的にその週の土曜日に調整する。
    /// </param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>発見したレース一覧。取得できなかった場合は空リスト。</returns>
    public async Task<IReadOnlyList<WeekendRaceInfo>> DiscoverAsync(
        DateOnly targetWeekend,
        CancellationToken cancellationToken = default)
    {
        var saturday = GetSaturday(targetWeekend);
        var sunday = saturday.AddDays(1);

        var prompt = $"""
            {saturday:yyyy年M月d日}（土）〜{sunday:M月d日}（日）に開催される
            すべての JRA レースを調査し、出走馬・騎手・調教師情報とともに JSON 配列で返してください。
            """;

        var result = await _innerAgent.RunAsync(prompt, cancellationToken: cancellationToken);
        return ParseRaceInfoList(result.Text);
    }

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private static DateOnly GetSaturday(DateOnly date)
    {
        var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(daysUntilSaturday);
    }

    private static IReadOnlyList<WeekendRaceInfo> ParseRaceInfoList(string text)
    {
        var jsonText = ExtractJsonArray(text);
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return [];
        }

        try
        {
            var dtos = JsonSerializer.Deserialize<List<WeekendRaceInfoDto>>(
                jsonText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return dtos?.ConvertAll(ToWeekendRaceInfo) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ExtractJsonArray(string text)
    {
        // ```json ... ``` ブロックを優先して探す
        const string codeBlockStart = "```json";
        const string codeBlockEnd = "```";
        var startIdx = text.IndexOf(codeBlockStart, StringComparison.Ordinal);
        if (startIdx >= 0)
        {
            var contentStart = startIdx + codeBlockStart.Length;
            var endIdx = text.IndexOf(codeBlockEnd, contentStart, StringComparison.Ordinal);
            if (endIdx > contentStart)
            {
                return text[contentStart..endIdx].Trim();
            }
        }

        // フォールバック: 先頭の '[' から末尾の ']' を探す
        var arrayStart = text.IndexOf('[');
        var arrayEnd = text.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            return text[arrayStart..(arrayEnd + 1)];
        }

        return string.Empty;
    }

    private static WeekendRaceInfo ToWeekendRaceInfo(WeekendRaceInfoDto dto) =>
        new(
            RaceName: dto.RaceName ?? string.Empty,
            RaceDate: DateOnly.TryParse(dto.RaceDate, out var d)
                ? d
                : DateOnly.FromDateTime(DateTime.Today),
            Racecourse: dto.Racecourse ?? string.Empty,
            RaceNumber: dto.RaceNumber,
            RaceQuery: dto.RaceQuery ?? dto.RaceName ?? string.Empty,
            HorseNames: dto.HorseNames ?? [],
            JockeyNames: dto.JockeyNames ?? [],
            TrainerNames: dto.TrainerNames ?? []);

    // ------------------------------------------------------------------ //
    // internal DTO for JSON deserialization
    // ------------------------------------------------------------------ //

    private sealed class WeekendRaceInfoDto
    {
        [JsonPropertyName("raceName")]
        public string? RaceName { get; set; }

        [JsonPropertyName("raceDate")]
        public string? RaceDate { get; set; }

        [JsonPropertyName("racecourse")]
        public string? Racecourse { get; set; }

        [JsonPropertyName("raceNumber")]
        public int RaceNumber { get; set; }

        [JsonPropertyName("raceQuery")]
        public string? RaceQuery { get; set; }

        [JsonPropertyName("horseNames")]
        public List<string>? HorseNames { get; set; }

        [JsonPropertyName("jockeyNames")]
        public List<string>? JockeyNames { get; set; }

        [JsonPropertyName("trainerNames")]
        public List<string>? TrainerNames { get; set; }
    }
}
