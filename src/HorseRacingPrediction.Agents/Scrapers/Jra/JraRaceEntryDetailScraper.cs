using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA の出馬表ページ上で馬名・騎手名・調教師名をクリックし、
/// 各プロフィールページを要素操作のみで往復しながら詳細情報を収集する。
/// </summary>
public sealed class JraRaceEntryDetailScraper
{
    private static readonly string[] AffiliationCodes = ["美浦", "栗東", "地方", "JRA"];

    private readonly IWebBrowser _browser;
    private readonly JraRaceCardScraper _raceCardScraper;

    public JraRaceEntryDetailScraper(IWebBrowser browser, JraRaceCardScraper raceCardScraper)
    {
        _browser = browser;
        _raceCardScraper = raceCardScraper;
    }

    public async Task<JraRaceCardDetailData?> ScrapeAsync(
        string raceCardUrl,
        int? maxEntries = null,
        CancellationToken cancellationToken = default)
    {
        var raceCard = await _raceCardScraper.ScrapeAsync(raceCardUrl, cancellationToken);
        if (raceCard is null)
        {
            return null;
        }

        return await ScrapeAsync(raceCard, maxEntries, cancellationToken);
    }

    /// <summary>
    /// ブラウザが既に出馬表ページを表示している状態でエントリープロフィールを収集する。
    /// クリックで遷移済みの場合に再ナビゲーションを省略するために使用する。
    /// </summary>
    public async Task<JraRaceCardDetailData?> ScrapeAsync(
        JraRaceCardData raceCard,
        int? maxEntries = null,
        CancellationToken cancellationToken = default)
    {
        var entryProfiles = new List<JraRaceEntryProfileData>();
        var horseCache = new Dictionary<string, JraHorseProfileData?>(StringComparer.Ordinal);
        var jockeyCache = new Dictionary<string, JraJockeyProfileData?>(StringComparer.Ordinal);
        var trainerCache = new Dictionary<string, JraTrainerProfileData?>(StringComparer.Ordinal);

        var effectiveEntries = maxEntries.HasValue && maxEntries.Value > 0
            ? raceCard.Entries.Take(maxEntries.Value)
            : raceCard.Entries;

        foreach (var entry in effectiveEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!horseCache.TryGetValue(entry.HorseName, out var horseProfile))
            {
                horseProfile = await TryScrapeHorseProfileAsync(entry, cancellationToken);
                horseCache[entry.HorseName] = horseProfile;
            }

            JraJockeyProfileData? jockeyProfile = null;
            if (!string.IsNullOrWhiteSpace(entry.JockeyName))
            {
                if (!jockeyCache.TryGetValue(entry.JockeyName, out jockeyProfile))
                {
                    jockeyProfile = await TryScrapeJockeyProfileAsync(entry.JockeyName, cancellationToken);
                    jockeyCache[entry.JockeyName] = jockeyProfile;
                }
            }

            JraTrainerProfileData? trainerProfile = null;
            if (!string.IsNullOrWhiteSpace(entry.TrainerName))
            {
                if (!trainerCache.TryGetValue(entry.TrainerName, out trainerProfile))
                {
                    trainerProfile = await TryScrapeTrainerProfileAsync(entry.TrainerName, cancellationToken);
                    trainerCache[entry.TrainerName] = trainerProfile;
                }
            }

            entryProfiles.Add(new JraRaceEntryProfileData(entry, horseProfile, jockeyProfile, trainerProfile));
        }

        return new JraRaceCardDetailData(raceCard, entryProfiles);
    }

    private async Task<JraHorseProfileData?> TryScrapeHorseProfileAsync(
        JraRaceEntryData entry,
        CancellationToken cancellationToken)
    {
        return await TryScrapeProfileAsync(
            [entry.HorseName],
            snapshot => ParseHorseProfile(snapshot, entry),
            cancellationToken);
    }

    private async Task<JraJockeyProfileData?> TryScrapeJockeyProfileAsync(
        string jockeyName,
        CancellationToken cancellationToken)
    {
        return await TryScrapeProfileAsync(
            [jockeyName],
            snapshot => ParseJockeyProfile(snapshot, jockeyName),
            cancellationToken);
    }

    private async Task<JraTrainerProfileData?> TryScrapeTrainerProfileAsync(
        string rawTrainerName,
        CancellationToken cancellationToken)
    {
        var candidates = BuildTrainerClickCandidates(rawTrainerName);
        return await TryScrapeProfileAsync(
            candidates,
            snapshot => ParseTrainerProfile(snapshot, rawTrainerName),
            cancellationToken);
    }

    private async Task<T?> TryScrapeProfileAsync<T>(
        IReadOnlyList<string> clickCandidates,
        Func<PageSnapshot, T?> parse,
        CancellationToken cancellationToken)
    {
        if (!await TryClickAnyAsync(clickCandidates, cancellationToken))
        {
            return default;
        }

        try
        {
            var snapshot = await _browser.GetPageSnapshotAsync(maxLinks: 1, cancellationToken: cancellationToken);
            return parse(snapshot);
        }
        finally
        {
            await _browser.GoBackAsync(cancellationToken);
        }
    }

    private async Task<bool> TryClickAnyAsync(
        IReadOnlyList<string> clickCandidates,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in clickCandidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)).Distinct(StringComparer.Ordinal))
        {
            try
            {
                await _browser.ClickAsync(candidate, cancellationToken);
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static JraHorseProfileData ParseHorseProfile(PageSnapshot snapshot, JraRaceEntryData entry)
    {
        var facts = ExtractFacts(snapshot);
        AugmentFactsFromMainText(snapshot.MainText, facts);
        var registeredName = ExtractDisplayName(snapshot, entry.HorseName);
        var sexAgeValue = FindFactValue(facts, "性齢", "性別");
        var sexCode = ParseSexCode(sexAgeValue) ?? ParseSexCode(entry.SexAge);

        return new JraHorseProfileData(
            RegisteredName: registeredName,
            SexCode: sexCode,
            BirthDate: ParseDateOnly(FindFactValue(facts, "生年月日", "生年月")),
            TrainerName: FindFactValue(facts, "調教師", "厩舎"),
            OwnerName: FindFactValue(facts, "馬主", "オーナー"),
            BreederName: FindFactValue(facts, "生産者", "生産牧場"),
            SireName: FindFactValue(facts, "父"),
            DamName: FindFactValue(facts, "母"),
            Facts: facts);
    }

    private static JraJockeyProfileData ParseJockeyProfile(PageSnapshot snapshot, string fallbackName)
    {
        var facts = ExtractFacts(snapshot);
        AugmentFactsFromMainText(snapshot.MainText, facts);
        return new JraJockeyProfileData(
            DisplayName: ExtractDisplayName(snapshot, fallbackName),
            AffiliationCode: ExtractAffiliationCode(FindFactValue(facts, "所属", "所属厩舎", "拠点")),
            BirthDate: ParseDateOnly(FindFactValue(facts, "生年月日", "誕生日")),
            DebutYear: ParseYear(FindFactValue(facts, "デビュー年", "初騎乗", "デビュー")),
            Facts: facts);
    }

    private static JraTrainerProfileData ParseTrainerProfile(PageSnapshot snapshot, string fallbackName)
    {
        var facts = ExtractFacts(snapshot);
        AugmentFactsFromMainText(snapshot.MainText, facts);
        return new JraTrainerProfileData(
            DisplayName: ExtractDisplayName(snapshot, ParseAffiliatedName(fallbackName).DisplayName ?? fallbackName),
            AffiliationCode: ExtractAffiliationCode(
                FindFactValue(facts, "所属", "所属厩舎", "拠点") ?? ParseAffiliatedName(fallbackName).AffiliationCode),
            DebutYear: ParseYear(FindFactValue(facts, "デビュー年", "開業", "初出走")),
            Facts: facts);
    }

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
                    for (var index = 0; index < row.Count - 1; index += 2)
                    {
                        AddFact(facts, row[index], row[index + 1]);
                    }

                    continue;
                }

                if (table.Headers.Count == row.Count && row.Count > 0 && row.Count <= 8)
                {
                    for (var index = 0; index < row.Count; index++)
                    {
                        AddFact(facts, table.Headers[index], row[index]);
                    }
                }
            }
        }

        return facts;
    }

    private static void AugmentFactsFromMainText(string? mainText, Dictionary<string, string> facts)
    {
        if (string.IsNullOrWhiteSpace(mainText)) return;

        // 性別: "性別 牡"
        TryAddFromMatch(facts, "性別", Regex.Match(mainText, @"性別\s+([牡牝騸セ])"));

        // 生年月日: "生年月日 2023年3月10日"
        TryAddFromMatch(facts, "生年月日", Regex.Match(mainText, @"生年月日\s+(\d{4}年\d{1,2}月\d{1,2}日)"));

        // 調教師名: "調教師名 水野 貴広（美浦）" → "水野 貴広" (parens excluded)
        TryAddFromMatch(facts, "調教師名", Regex.Match(mainText, @"調教師名\s+([\p{L}\p{N}]+(?:\s+[\p{L}\p{N}]+)?)"));

        // 馬主名: stop before "母 ", "馬齢", "調教師名", "生年月日"
        TryAddFromMatch(facts, "馬主名", Regex.Match(mainText,
            @"馬主名\s+(.+?)(?=\s+母\s+|\s+馬齢\s+|\s+調教師名\s+|\s+母の父\s+|\s+生年月日\s+|$)",
            RegexOptions.Singleline));

        // 生産牧場
        TryAddFromMatch(facts, "生産牧場", Regex.Match(mainText, @"生産牧場\s+(\S+)"));

        // 父: "父 インディチャンプ" — preceded by space/start (to exclude "母の父")
        TryAddFromMatch(facts, "父", Regex.Match(mainText, @"(?:^|[ 　\n])父\s+(\S+)"));

        // 母: "母 アルマカーテナ" — "母" must be followed by whitespace (to exclude "母の父","母の母")
        TryAddFromMatch(facts, "母", Regex.Match(mainText, @"(?:^|[ 　\n])母\s+(\S+)"));

        // 毛色
        TryAddFromMatch(facts, "毛色", Regex.Match(mainText, @"毛色\s+(\S+)"));

        // 馬齢
        TryAddFromMatch(facts, "馬齢", Regex.Match(mainText, @"馬齢\s+(\d+歳)"));
    }

    private static void TryAddFromMatch(Dictionary<string, string> facts, string key, Match match)
    {
        if (match.Success && !facts.ContainsKey(key))
        {
            var value = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                facts[key] = value;
        }
    }

    private static void AddFact(Dictionary<string, string> facts, string? rawLabel, string? rawValue)
    {
        var label = rawLabel?.Trim();
        var value = rawValue?.Trim();
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (label.Length > 32 || value == "-" || value == "--")
        {
            return;
        }

        facts[label] = value;
    }

    private static string ExtractDisplayName(PageSnapshot snapshot, string fallback)
    {
        var heading = snapshot.Headings
            .Select(heading => heading.Trim())
            .FirstOrDefault(heading => !string.IsNullOrWhiteSpace(heading) && heading.Length <= 40 && ContainsKanjiOrKana(heading));

        return string.IsNullOrWhiteSpace(heading)
            ? fallback
            : heading;
    }

    private static string? FindFactValue(IReadOnlyDictionary<string, string> facts, params string[] labels)
    {
        foreach (var label in labels)
        {
            var normalizedLabel = NormalizeLabel(label);
            var match = facts.FirstOrDefault(pair => NormalizeLabel(pair.Key).Contains(normalizedLabel, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                return match.Value;
            }
        }

        return null;
    }

    private static string NormalizeLabel(string value)
    {
        return value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("：", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string? ParseSexCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var first = value.Trim()[0];
        return first is '牡' or '牝' or 'セ' ? first.ToString() : null;
    }

    private static DateOnly? ParseDateOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(value, @"(\d{4})年(\d{1,2})月(\d{1,2})日");
        if (!match.Success)
        {
            return null;
        }

        if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            return null;
        }

        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static int? ParseYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(value, @"(19|20)\d{2}");
        return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? ExtractAffiliationCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return AffiliationCodes.FirstOrDefault(code => value.Contains(code, StringComparison.Ordinal));
    }

    private static (string? DisplayName, string? AffiliationCode) ParseAffiliatedName(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return (null, null);
        }

        var trimmed = rawValue.Trim();
        var affiliation = AffiliationCodes.FirstOrDefault(code => trimmed.Contains(code, StringComparison.Ordinal));
        if (affiliation is null)
        {
            return (trimmed, null);
        }

        var display = trimmed.Replace(affiliation, string.Empty, StringComparison.Ordinal)
            .Trim(' ', '　', '・', '･', '-', '−', '/');

        return (string.IsNullOrWhiteSpace(display) ? trimmed : display, affiliation);
    }

    private static IReadOnlyList<string> BuildTrainerClickCandidates(string rawTrainerName)
    {
        var parsed = ParseAffiliatedName(rawTrainerName);
        return [rawTrainerName, parsed.DisplayName ?? string.Empty];
    }

    private static bool ContainsKanjiOrKana(string value)
    {
        return value.Any(character =>
            character is >= '\u3040' and <= '\u30ff' or >= '\u4e00' and <= '\u9fff');
    }
}