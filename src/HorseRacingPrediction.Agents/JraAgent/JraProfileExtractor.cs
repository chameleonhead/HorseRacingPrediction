using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Agents.JraAgent;

/// <summary>
/// 競走馬情報・騎手情報・調教師情報ページから <see cref="JraEntityProfile"/> を抽出する。
/// テーブルのキー/バリューペアと MainText の正規表現補完を組み合わせる。
/// </summary>
public sealed class JraProfileExtractor : IPageExtractor
{
    public JraPageKind[] SupportedPageKinds =>
    [
        JraPageKind.HorseProfile,
        JraPageKind.JockeyProfile,
        JraPageKind.TrainerProfile,
    ];

    public async Task<object?> ExtractAsync(IWebBrowser browser, CancellationToken cancellationToken = default)
    {
        var snapshot = await browser.GetPageSnapshotAsync(maxLinks: 20, cancellationToken: cancellationToken);
        var url = browser.CurrentUrl ?? string.Empty;
        var kind = JraPageKindDetector.Detect(url, snapshot);
        return ParseProfile(snapshot, url, kind);
    }

    private static JraEntityProfile ParseProfile(PageSnapshot snapshot, string url, JraPageKind kind)
    {
        var entityKind = kind switch
        {
            JraPageKind.HorseProfile   => "horse",
            JraPageKind.JockeyProfile  => "jockey",
            JraPageKind.TrainerProfile => "trainer",
            _                          => "unknown",
        };

        var facts = ExtractFacts(snapshot);
        AugmentFactsFromMainText(snapshot.MainText, facts);

        var displayName = ExtractDisplayName(snapshot, facts);
        var sexCode     = FindFactValue(facts, "性別", "性齢")?.TrimStart() is { } sv
                          ? ParseSexCode(sv) : null;
        var birthDate   = ParseDateOnly(FindFactValue(facts, "生年月日", "誕生日", "生年月"));
        var affiliation = FindFactValue(facts, "所属", "所属厩舎", "拠点");
        var debutYear   = ParseYear(FindFactValue(facts, "デビュー年", "開業", "初騎乗", "デビュー", "初出走"));
        var sireName    = FindFactValue(facts, "父");
        var damName     = FindFactValue(facts, "母");
        var ownerName   = FindFactValue(facts, "馬主名", "馬主");
        var breederName = FindFactValue(facts, "生産牧場", "生産者");
        var trainerName = FindFactValue(facts, "調教師名", "調教師", "厩舎");

        return new JraEntityProfile(
            EntityKind: entityKind,
            DisplayName: displayName,
            SexCode: sexCode,
            BirthDate: birthDate,
            Affiliation: affiliation,
            DebutYear: debutYear,
            SireName: sireName,
            DamName: damName,
            OwnerName: ownerName,
            BreederName: breederName,
            TrainerName: trainerName,
            Facts: facts,
            SourceUrl: url);
    }

    // ──────────────── 事実テーブル抽出 ────────────────

    private static Dictionary<string, string> ExtractFacts(PageSnapshot snapshot)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var table in snapshot.Tables)
        {
            foreach (var row in table.Rows)
            {
                if (row.Count == 2)
                {
                    AddFact(facts, row[0], row[1]);
                    continue;
                }

                if (row.Count >= 4 && row.Count % 2 == 0)
                {
                    for (var i = 0; i < row.Count - 1; i += 2)
                        AddFact(facts, row[i], row[i + 1]);
                    continue;
                }

                if (table.Headers.Count == row.Count && row.Count is > 0 and <= 8)
                {
                    for (var i = 0; i < row.Count; i++)
                        AddFact(facts, table.Headers[i], row[i]);
                }
            }
        }

        return facts;
    }

    private static void AddFact(Dictionary<string, string> facts, string? rawLabel, string? rawValue)
    {
        var label = rawLabel?.Trim();
        var value = rawValue?.Trim();
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
            return;
        if (label.Length > 32 || value == "-" || value == "--")
            return;
        facts[label] = value;
    }

    // ──────────────── MainText 補完 ────────────────

    private static void AugmentFactsFromMainText(string? mainText, Dictionary<string, string> facts)
    {
        if (string.IsNullOrWhiteSpace(mainText)) return;

        TryAdd(facts, "性別",    Regex.Match(mainText, @"性別\s+([牡牝騸セ])"));
        TryAdd(facts, "生年月日", Regex.Match(mainText, @"生年月日\s+(\d{4}年\d{1,2}月\d{1,2}日)"));
        TryAdd(facts, "調教師名", Regex.Match(mainText, @"調教師名\s+([\p{L}\p{N}]+(?:\s+[\p{L}\p{N}]+)?)"));
        TryAdd(facts, "馬主名",   Regex.Match(mainText,
            @"馬主名\s+(.+?)(?=\s+母\s+|\s+馬齢\s+|\s+調教師名\s+|\s+母の父\s+|\s+生年月日\s+|$)",
            RegexOptions.Singleline));
        TryAdd(facts, "生産牧場", Regex.Match(mainText, @"生産牧場\s+(\S+)"));
        TryAdd(facts, "父",       Regex.Match(mainText, @"(?:^|[ 　\n])父\s+(\S+)"));
        TryAdd(facts, "母",       Regex.Match(mainText, @"(?:^|[ 　\n])母\s+(\S+)"));
        TryAdd(facts, "毛色",     Regex.Match(mainText, @"毛色\s+(\S+)"));
        TryAdd(facts, "馬齢",     Regex.Match(mainText, @"馬齢\s+(\d+歳)"));
    }

    private static void TryAdd(Dictionary<string, string> facts, string key, Match m,
        RegexOptions _ = RegexOptions.None)
    {
        if (!m.Success || facts.ContainsKey(key)) return;
        var v = m.Groups[1].Value.Trim();
        if (!string.IsNullOrWhiteSpace(v)) facts[key] = v;
    }

    // ──────────────── ヘルパー ────────────────

    private static string? ExtractDisplayName(PageSnapshot snapshot, IReadOnlyDictionary<string, string> facts)
    {
        var fromHeading = snapshot.Headings
            .Select(h => h.Trim())
            .FirstOrDefault(h => h.Length is > 0 and <= 40 && ContainsKanjiOrKana(h));
        if (fromHeading is not null) return fromHeading;

        return FindFactValue(facts, "馬名", "騎手名", "調教師名");
    }

    private static string? FindFactValue(IReadOnlyDictionary<string, string> facts, params string[] labels)
    {
        foreach (var label in labels)
        {
            var normalizedLabel = Normalize(label);
            var match = facts.FirstOrDefault(p => Normalize(p.Key).Contains(normalizedLabel, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(match.Value)) return match.Value;
        }
        return null;
    }

    private static string Normalize(string v)
        => v.Replace(" ", "", StringComparison.Ordinal)
            .Replace("　", "", StringComparison.Ordinal)
            .Replace(":", "", StringComparison.Ordinal)
            .Replace("：", "", StringComparison.Ordinal)
            .Trim();

    private static string? ParseSexCode(string? value)
    {
        return JraSexAgeParser.Parse(value).SexCode;
    }

    private static DateOnly? ParseDateOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // "2023年3月10日"
        var m = Regex.Match(value, @"(\d{4})年(\d{1,2})月(\d{1,2})日");
        if (m.Success
            && int.TryParse(m.Groups[1].Value, out var y)
            && int.TryParse(m.Groups[2].Value, out var mo)
            && int.TryParse(m.Groups[3].Value, out var d))
        {
            return new DateOnly(y, mo, d);
        }
        return null;
    }

    private static int? ParseYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var m = Regex.Match(value, @"\d{4}");
        return m.Success && int.TryParse(m.Value, out var y) ? y : null;
    }

    private static bool ContainsKanjiOrKana(string s)
        => s.Any(c => (c >= '\u3040' && c <= '\u30FF') || (c >= '\u4E00' && c <= '\u9FFF'));
}
