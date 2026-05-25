using System.Globalization;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.Scrapers.Jra;

/// <summary>
/// JRA 公式サイトの出馬表ページから構造化データを抽出するスクレイパー。
/// <para>
/// AIエージェントが検索・探索によって出馬表の URL を特定した後、
/// このスクレイパーがその URL に Playwright でアクセスし、
/// ページ内のテーブル構造を解析して <see cref="JraRaceCardData"/> として返す。
/// </para>
/// <para>
/// テーブルが取得できない場合（ページ構造変更・認証が必要なページなど）は
/// エントリを空のまま返す。
/// </para>
/// </summary>
public sealed class JraRaceCardScraper : IScraper<JraRaceCardData>
{
    private static readonly string[] RacecourseNames =
    [
        "東京", "中山", "阪神", "京都", "中京", "小倉", "函館", "福島", "新潟", "札幌"
    ];

    private static readonly string[] RaceCardRequiredHeaders = ["馬番", "馬名", "競走馬"];

    private readonly IWebBrowser _browser;

    public JraRaceCardScraper(IWebBrowser browser)
    {
        _browser = browser;
    }

    /// <inheritdoc />
    public async Task<JraRaceCardData?> ScrapeAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        await _browser.NavigateAsync(url, cancellationToken);
        var snapshot = await _browser.GetPageSnapshotAsync(1, cancellationToken);

        if (IsKnownErrorPage(url, snapshot))
        {
            return null;
        }

        var metadata = ParseRaceMetadata(snapshot);
        var entries = ParseEntries(snapshot.Tables);

        return new JraRaceCardData(
            Url: url,
            RaceName: metadata.RaceName,
            Racecourse: metadata.Racecourse,
            RaceDate: metadata.RaceDate,
            RaceNumber: metadata.RaceNumber,
            MeetingNumber: metadata.MeetingNumber,
            DayNumber: metadata.DayNumber,
            PostTime: metadata.PostTime,
            ConditionSummary: metadata.ConditionSummary,
            AgeCondition: metadata.AgeCondition,
            AgeConditionCode: metadata.AgeConditionCode,
            RaceClass: metadata.RaceClass,
            RaceClassCode: metadata.RaceClassCode,
            Eligibility: metadata.Eligibility,
            EligibilityCodes: metadata.EligibilityCodes,
            EntryCondition: metadata.EntryCondition,
            EntryConditionCodes: metadata.EntryConditionCodes,
            WeightCondition: metadata.WeightCondition,
            WeightConditionCode: metadata.WeightConditionCode,
            CourseType: metadata.CourseType,
            TrackDirection: metadata.TrackDirection,
            Distance: metadata.Distance,
            Grade: metadata.Grade,
            PrizeMoney: metadata.PrizeMoney,
            Entries: entries);
    }

    /// <summary>
    /// ブラウザが既に出馬表ページを表示している状態でページを解析する。
    /// クリックで遷移した直後に呼び出すことで URL ナビゲーションを省略できる。
    /// </summary>
    public async Task<JraRaceCardData?> ScrapeCurrentPageAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _browser.GetPageSnapshotAsync(1, cancellationToken);
        var url = _browser.CurrentUrl ?? string.Empty;

        if (IsKnownErrorPage(url, snapshot))
        {
            return null;
        }

        var metadata = ParseRaceMetadata(snapshot);
        var entries = ParseEntries(snapshot.Tables);

        return new JraRaceCardData(
            Url: url,
            RaceName: metadata.RaceName,
            Racecourse: metadata.Racecourse,
            RaceDate: metadata.RaceDate,
            RaceNumber: metadata.RaceNumber,
            MeetingNumber: metadata.MeetingNumber,
            DayNumber: metadata.DayNumber,
            PostTime: metadata.PostTime,
            ConditionSummary: metadata.ConditionSummary,
            AgeCondition: metadata.AgeCondition,
            AgeConditionCode: metadata.AgeConditionCode,
            RaceClass: metadata.RaceClass,
            RaceClassCode: metadata.RaceClassCode,
            Eligibility: metadata.Eligibility,
            EligibilityCodes: metadata.EligibilityCodes,
            EntryCondition: metadata.EntryCondition,
            EntryConditionCodes: metadata.EntryConditionCodes,
            WeightCondition: metadata.WeightCondition,
            WeightConditionCode: metadata.WeightConditionCode,
            CourseType: metadata.CourseType,
            TrackDirection: metadata.TrackDirection,
            Distance: metadata.Distance,
            Grade: metadata.Grade,
            PrizeMoney: metadata.PrizeMoney,
            Entries: entries);
    }

    // ------------------------------------------------------------------ //
    // メタ情報の解析
    // ------------------------------------------------------------------ //

    private static RaceMetadata ParseRaceMetadata(PageSnapshot snapshot)
    {
        var headingsText = string.Join("\n", snapshot.Headings);
        var searchText = $"{snapshot.Title}\n{headingsText}\n{snapshot.MainText}";
        var contentLines = ExtractContentLines(snapshot);

        var raceName = ExtractRaceName(snapshot, contentLines);
        var meeting = ExtractMeetingInfo(headingsText) ?? ExtractMeetingInfo(searchText);
        var conditionSummary = ExtractConditionSummary(searchText, raceName, contentLines);
        var courseSummary = contentLines.FirstOrDefault(x => x.Contains("コース：", StringComparison.Ordinal)) ?? searchText;
        var (eligibilityTokens, entryConditionTokens) = ExtractConditionTokens(conditionSummary);
        var raceCourse = meeting?.Racecourse ?? ExtractRacecourse(headingsText) ?? ExtractRacecourse(searchText);
        var raceDate = ExtractDate(headingsText) ?? ExtractDate(searchText);
        var raceNumber = ExtractRaceNumber(headingsText) ?? ExtractRaceNumber(searchText);
        var postTime = ExtractPostTime(searchText);
        var ageCondition = ExtractAgeCondition(conditionSummary, raceName);
        var raceClass = ExtractRaceClass(conditionSummary, raceName);
        var eligibility = eligibilityTokens.Count == 0 ? null : string.Join(" ", eligibilityTokens);
        var entryCondition = entryConditionTokens.Count == 0 ? null : string.Join(" ", entryConditionTokens);
        var weightCondition = ExtractWeightCondition(conditionSummary);
        var courseType = ExtractCourseType(courseSummary);
        var trackDirection = ExtractTrackDirection(courseSummary);
        var distance = ExtractDistance(courseSummary);
        var grade = ExtractGrade(searchText);
        var prizeMoney = ExtractPrizeMoney(searchText, contentLines);

        return new RaceMetadata(
            raceName,
            raceCourse,
            raceDate,
            raceNumber,
            meeting?.MeetingNumber,
            meeting?.DayNumber,
            postTime,
            conditionSummary,
            ageCondition,
            NormalizeAgeConditionCode(ageCondition),
            raceClass,
            NormalizeRaceClassCode(raceClass),
            eligibility,
            eligibilityTokens.Select(NormalizeEligibilityCode).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
            entryCondition,
            entryConditionTokens.Select(NormalizeEntryConditionCode).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
            weightCondition,
            NormalizeWeightConditionCode(weightCondition),
            courseType,
            trackDirection,
            distance,
            grade,
            prizeMoney);
    }

    private static bool IsKnownErrorPage(string url, PageSnapshot snapshot)
    {
        if (url.Contains("/error/", StringComparison.OrdinalIgnoreCase)
            || url.Contains("error013", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var title = snapshot.Title ?? string.Empty;
        var text = $"{title}\n{snapshot.MainText}\n{string.Join("\n", snapshot.Headings)}";

        return text.Contains("パラメータエラー", StringComparison.Ordinal)
            || text.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
            || text.Contains("アクセスしたページ", StringComparison.Ordinal)
            || text.Contains("お探しのページ", StringComparison.Ordinal)
            || text.Contains("ただいまご利用できません", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ExtractContentLines(PageSnapshot snapshot)
    {
        var source = string.Join(
            "\n",
            new[] { snapshot.Title ?? string.Empty }
                .Concat(snapshot.Headings)
                .Append(snapshot.MainText));

        return source
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static string ExtractRaceName(PageSnapshot snapshot, IReadOnlyList<string> contentLines)
    {
        var headingRaceName = snapshot.Headings
            .Select(CleanRaceName)
            .FirstOrDefault(IsLikelyRaceName);
        if (!string.IsNullOrWhiteSpace(headingRaceName))
        {
            return headingRaceName;
        }

        var combinedText = string.Join("\n", snapshot.Headings.Append(snapshot.MainText));
        var raceLikeMatch = Regex.Match(
            combinedText,
            @"(?:第\d+回\s*)?(?<name>[^\s\r\n]+?)\s+G[ⅠⅡⅢ1-3]",
            RegexOptions.CultureInvariant);
        if (raceLikeMatch.Success && IsLikelyRaceName(raceLikeMatch.Groups["name"].Value))
        {
            return CleanRaceName(raceLikeMatch.Groups["name"].Value);
        }

        var namedRaceMatch = Regex.Match(
            combinedText,
            @"(?:第\d+回\s*)?(?<name>[^\s\r\n]+(?:記念|カップ|ステークス|賞|S))",
            RegexOptions.CultureInvariant);
        if (namedRaceMatch.Success && IsLikelyRaceName(namedRaceMatch.Groups["name"].Value))
        {
            return CleanRaceName(namedRaceMatch.Groups["name"].Value);
        }

        var genericRaceName = contentLines
            .Select(CleanRaceName)
            .FirstOrDefault(IsLikelyGenericRaceName);
        if (!string.IsNullOrWhiteSpace(genericRaceName))
        {
            return genericRaceName;
        }

        var candidates = snapshot.Headings
            .Select(h => h.Trim())
            .Where(h => h.Length > 1)
            .Where(h => !IsBoilerplateHeading(h))
            .Where(h => !IsCourseLine(h))
            .Where(h => !IsDateRaceNumberLine(h))
            .ToList();

        var raceName = candidates.FirstOrDefault(h => ContainsKanji(h))
            ?? snapshot.Title?.Trim()
            ?? string.Empty;

        return CleanRaceName(raceName);
    }

    private static string? ExtractRacecourse(string text)
    {
        return RacecourseNames.FirstOrDefault(rc => text.Contains(rc, StringComparison.Ordinal));
    }

    private static string? ExtractRacecourse(PageSnapshot snapshot)
    {
        var source = string.Join("\n", snapshot.Headings.Append(snapshot.MainText));
        var match = Regex.Match(source, @"\d+回(?<raceCourse>東京|中山|阪神|京都|中京|小倉|函館|福島|新潟|札幌)\d+日");
        return match.Success ? match.Groups["raceCourse"].Value : null;
    }

    private static DateOnly? ExtractDate(string text)
    {
        var match = Regex.Match(text, @"(\d{4})年(\d{1,2})月(\d{1,2})日");
        if (!match.Success)
        {
            return null;
        }

        if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) &&
            int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) &&
            int.TryParse(match.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            try
            {
                return new DateOnly(year, month, day);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }

    private static int? ExtractRaceNumber(string text)
    {
        var match = Regex.Match(text, @"(\d{1,2})R\b");
        if (!match.Success)
        {
            match = Regex.Match(text, @"第(\d{1,2})レース");
        }

        if (!match.Success)
        {
            match = Regex.Match(text, @"(\d{1,2})レース");
        }

        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num)
            ? num
            : null;
    }

    private static string? ExtractCourseType(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.Contains("ダート", StringComparison.Ordinal))
        {
            return "ダート";
        }

        if (text.Contains("障害", StringComparison.Ordinal))
        {
            return "障害";
        }

        if (text.Contains("芝", StringComparison.Ordinal))
        {
            return "芝";
        }

        return null;
    }

    private static int? ExtractDistance(string text)
    {
        var match = Regex.Match(text, @"(?<distance>\d{1,2},\d{3}|\d{3,4})\s*(?:[mM]|メートル)");
        if (!match.Success)
        {
            return null;
        }

        var normalized = match.Groups["distance"].Value.Replace(",", string.Empty, StringComparison.Ordinal);
        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dist)
            ? dist
            : null;
    }

    private static TimeOnly? ExtractPostTime(string text)
    {
        var match = Regex.Match(text, @"発走時刻[:：]\s*(?<hour>\d{1,2})時(?<minute>\d{1,2})分");
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["hour"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour)
            && int.TryParse(match.Groups["minute"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute)
            ? new TimeOnly(hour, minute)
            : null;
    }

    private static MeetingInfo? ExtractMeetingInfo(string text)
    {
        var match = Regex.Match(text, @"(?<meeting>\d+)回(?<raceCourse>東京|中山|阪神|京都|中京|小倉|函館|福島|新潟|札幌)(?<day>\d+)日");
        if (!match.Success)
        {
            return null;
        }

        if (!int.TryParse(match.Groups["meeting"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var meetingNumber)
            || !int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dayNumber))
        {
            return null;
        }

        return new MeetingInfo(meetingNumber, match.Groups["raceCourse"].Value, dayNumber);
    }

    private static string? ExtractConditionSummary(string searchText, string raceName, IReadOnlyList<string> contentLines)
    {
        var normalized = NormalizeWhitespace(searchText);
        var courseIndex = normalized.IndexOf("コース：", StringComparison.Ordinal);
        if (courseIndex >= 0)
        {
            var start = 0;
            if (!string.IsNullOrWhiteSpace(raceName))
            {
                var searchWindow = normalized[..courseIndex];
                var raceNameIndex = searchWindow.LastIndexOf(raceName, StringComparison.Ordinal);
                if (raceNameIndex >= 0 && raceNameIndex < courseIndex)
                {
                    start = raceNameIndex + raceName.Length;
                }
            }

            var ageMatch = Regex.Match(normalized[..courseIndex], @"(?:障害)?\d+歳(?:以上|上)?", RegexOptions.CultureInvariant);
            if (ageMatch.Success)
            {
                start = Math.Max(start, ageMatch.Index);
            }

            var summary = normalized[start..courseIndex].Trim();
            return string.IsNullOrWhiteSpace(summary) ? null : summary;
        }

        return contentLines
            .Select(x =>
            {
                var index = x.IndexOf("コース：", StringComparison.Ordinal);
                return index >= 0 ? x[..index].Trim() : x;
            })
            .FirstOrDefault(x => x.Contains("馬齢", StringComparison.Ordinal)
                || x.Contains("ハンデ", StringComparison.Ordinal)
                || x.Contains("別定", StringComparison.Ordinal)
                || x.Contains("定量", StringComparison.Ordinal))
            ?? contentLines.FirstOrDefault(x => x.Contains("馬齢", StringComparison.Ordinal)
                || x.Contains("ハンデ", StringComparison.Ordinal)
                || x.Contains("別定", StringComparison.Ordinal)
                || x.Contains("定量", StringComparison.Ordinal));
    }

    private static string? ExtractAgeCondition(string? conditionsLine, string raceName)
    {
        var source = string.Join(" ", new[] { conditionsLine, raceName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var match = Regex.Match(source, @"(?<age>(?:障害)?\d+歳(?:以上|上)?)");
        return match.Success ? match.Groups["age"].Value : null;
    }

    private static string? ExtractRaceClass(string? conditionsLine, string raceName)
    {
        var source = string.Join(" ", new[] { conditionsLine, raceName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var match = Regex.Match(source, @"(?<class>未勝利|新馬|未出走|1勝クラス|2勝クラス|3勝クラス|オープン)");
        return match.Success ? match.Groups["class"].Value : null;
    }

    private static (IReadOnlyList<string> EligibilityTokens, IReadOnlyList<string> EntryConditionTokens) ExtractConditionTokens(string? conditionSummary)
    {
        if (string.IsNullOrWhiteSpace(conditionSummary))
        {
            return (Array.Empty<string>(), Array.Empty<string>());
        }

        var targetText = conditionSummary;
        var courseIndex = targetText.IndexOf("コース：", StringComparison.Ordinal);
        if (courseIndex >= 0)
        {
            targetText = targetText[..courseIndex];
        }

        var eligibilityTokens = new List<string>();
        var entryConditionTokens = new List<string>();
        var matches = Regex.Matches(targetText, @"（(?<paren>[^）]+)）|［(?<square>[^］]+)］|(?<standalone>牝|牡)", RegexOptions.CultureInvariant);
        foreach (Match match in matches)
        {
            var token = match.Groups["paren"].Success
                ? match.Groups["paren"].Value.Trim()
                : match.Groups["square"].Success
                    ? match.Groups["square"].Value.Trim()
                    : match.Groups["standalone"].Value.Trim();

            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (IsEntryConditionToken(token))
            {
                if (!entryConditionTokens.Contains(token, StringComparer.Ordinal))
                {
                    entryConditionTokens.Add(token);
                }

                continue;
            }

            if (!eligibilityTokens.Contains(token, StringComparer.Ordinal))
            {
                eligibilityTokens.Add(token);
            }
        }

        return (eligibilityTokens, entryConditionTokens);
    }

    private static string? ExtractWeightCondition(string? conditionsLine)
    {
        var match = Regex.Match(conditionsLine ?? string.Empty, @"(?<weightCondition>馬齢|別定|定量|ハンデ)");
        return match.Success ? match.Groups["weightCondition"].Value : null;
    }

    private static string? ExtractTrackDirection(string text)
    {
        var match = Regex.Match(text, @"(?:芝|ダート|障害)・(?<direction>右|左|直線)");
        return match.Success ? match.Groups["direction"].Value : null;
    }

    private static IReadOnlyList<JraRacePrizeData> ExtractPrizeMoney(string searchText, IReadOnlyList<string> contentLines)
    {
        var prizes = new List<JraRacePrizeData>();
        var source = NormalizeWhitespace(searchText);
        var sectionMatches = PrizeSectionRegex.Matches(source);
        foreach (Match sectionMatch in sectionMatches)
        {
            var label = sectionMatch.Groups["label"].Value;
            var prizeText = sectionMatch.Groups["body"].Value;
            prizes.AddRange(ParsePrizeEntries(label, prizeText));
        }

        return prizes;
    }

    private static IReadOnlyList<JraRacePrizeData> ParsePrizeEntries(string label, string prizeText)
    {
        if (string.IsNullOrWhiteSpace(prizeText))
        {
            return [];
        }

        var matches = PrizeEntryRegex.Matches(prizeText);
        if (matches.Count == 0)
        {
            return [];
        }

        var prizes = new List<JraRacePrizeData>();
        var expectedPlace = 1;
        foreach (Match match in matches)
        {
            if (!int.TryParse(match.Groups["place"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var place)
                || !decimal.TryParse(match.Groups["amount"].Value.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                continue;
            }

            if (place != expectedPlace)
            {
                break;
            }

            prizes.Add(new JraRacePrizeData(label, place, amount));
            expectedPlace++;
            if (expectedPlace > 5)
            {
                break;
            }
        }

        return prizes;
    }

    private static string? ExtractGrade(string text)
    {
        if (text.Contains("GⅠ", StringComparison.Ordinal) || text.Contains("G1", StringComparison.Ordinal))
        {
            return "GⅠ";
        }

        if (text.Contains("GⅡ", StringComparison.Ordinal) || text.Contains("G2", StringComparison.Ordinal))
        {
            return "GⅡ";
        }

        if (text.Contains("GⅢ", StringComparison.Ordinal) || text.Contains("G3", StringComparison.Ordinal))
        {
            return "GⅢ";
        }

        if (text.Contains("重賞", StringComparison.Ordinal))
        {
            return "重賞";
        }

        return null;
    }

    private static bool IsEntryConditionToken(string token)
        => token is "指定" or "特指";

    private static string? NormalizeAgeConditionCode(string? ageCondition)
    {
        if (string.IsNullOrWhiteSpace(ageCondition))
        {
            return null;
        }

        var match = Regex.Match(ageCondition, @"^(?<jump>障害)?(?<age>\d+)歳(?<range>以上|上)?$", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return ageCondition;
        }

        var prefix = match.Groups["jump"].Success ? "jump-" : string.Empty;
        var age = match.Groups["age"].Value;
        return match.Groups["range"].Success ? $"{prefix}{age}up" : $"{prefix}{age}";
    }

    private static string? NormalizeRaceClassCode(string? raceClass)
        => raceClass switch
        {
            "未勝利" => "maiden",
            "新馬" => "debut",
            "未出走" => "not-started",
            "1勝クラス" => "1-win",
            "2勝クラス" => "2-win",
            "3勝クラス" => "3-win",
            "オープン" => "open",
            _ => raceClass,
        };

    private static string NormalizeEligibilityCode(string token)
        => token switch
        {
            "混合" => "mixed",
            "牝" => "fillies",
            "牡" => "colts",
            "国際" => "international",
            _ => token,
        };

    private static string NormalizeEntryConditionCode(string token)
        => token switch
        {
            "指定" => "designated",
            "特指" => "special-designated",
            _ => token,
        };

    private static string? NormalizeWeightConditionCode(string? weightCondition)
        => weightCondition switch
        {
            "馬齢" => "age-weight",
            "別定" => "special-weight",
            "定量" => "set-weight",
            "ハンデ" => "handicap",
            _ => weightCondition,
        };

    private static string NormalizeWhitespace(string text)
        => Regex.Replace(text ?? string.Empty, @"\s+", " ", RegexOptions.CultureInvariant).Trim();

    private static bool IsCourseLine(string text) =>
        Regex.IsMatch(text, @"\d{3,4}\s*[mM]") ||
        text.Contains("芝", StringComparison.Ordinal) ||
        text.Contains("ダート", StringComparison.Ordinal);

    private static bool IsDateRaceNumberLine(string text) =>
        (text.Contains("年", StringComparison.Ordinal) &&
         text.Contains("月", StringComparison.Ordinal) &&
         text.Contains("日", StringComparison.Ordinal)) ||
        Regex.IsMatch(text, @"\d{1,2}R\b");

    private static bool ContainsKanji(string text) =>
        text.Any(c => c >= '\u4e00' && c <= '\u9fff');

    private static bool IsBoilerplateHeading(string text)
        => text.Contains("JRA 日本中央競馬会", StringComparison.Ordinal)
           || text.Contains("日本中央競馬会", StringComparison.Ordinal)
           || text.StartsWith("出馬表", StringComparison.Ordinal)
           || text.Contains("検索ウィンドウ", StringComparison.Ordinal)
           || text.Contains("競馬メニュー", StringComparison.Ordinal)
           || text.Contains("ニュース", StringComparison.Ordinal)
           || text.Contains("コースレコード", StringComparison.Ordinal)
           || text.Contains("非当選・非抽選馬情報", StringComparison.Ordinal)
           || text.Contains("非当選馬", StringComparison.Ordinal)
           || text.Contains("非抽選馬", StringComparison.Ordinal)
           || text.Contains("JRAからのお知らせ", StringComparison.Ordinal);

    private static bool IsLikelyRaceName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = CleanRaceName(text);
        var normalizedForSuffix = Regex.Replace(cleaned, @"（[^）]+）$", string.Empty, RegexOptions.CultureInvariant);
        if (string.IsNullOrWhiteSpace(cleaned)
            || IsBoilerplateHeading(cleaned)
            || cleaned.StartsWith("本賞", StringComparison.Ordinal)
            || cleaned.StartsWith("付加賞", StringComparison.Ordinal)
            || cleaned == "更新")
        {
            return false;
        }

        return normalizedForSuffix.EndsWith("記念", StringComparison.Ordinal)
            || normalizedForSuffix.EndsWith("カップ", StringComparison.Ordinal)
            || normalizedForSuffix.EndsWith("トロフィー", StringComparison.Ordinal)
            || normalizedForSuffix.EndsWith("ステークス", StringComparison.Ordinal)
            || normalizedForSuffix.EndsWith("新聞杯", StringComparison.Ordinal)
            || normalizedForSuffix.EndsWith("賞", StringComparison.Ordinal)
            || normalizedForSuffix.EndsWith("S", StringComparison.Ordinal);
    }

    private static bool IsLikelyGenericRaceName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = CleanRaceName(text);
        if (IsBoilerplateHeading(cleaned)
            || IsCourseLine(cleaned)
            || IsDateRaceNumberLine(cleaned)
            || cleaned.StartsWith("本賞金", StringComparison.Ordinal)
            || cleaned.StartsWith("発走時刻", StringComparison.Ordinal)
            || cleaned.Contains("非当選", StringComparison.Ordinal)
            || cleaned.Contains("非抽選", StringComparison.Ordinal)
            || cleaned.Contains("コース：", StringComparison.Ordinal))
        {
            return false;
        }

        return Regex.IsMatch(cleaned, @"^(?:障害)?\d+歳(?:以上|上)?(?:未勝利|新馬|未出走|1勝クラス|2勝クラス|3勝クラス|オープン)$");
    }

    private static string CleanRaceName(string name)
    {
        name = Regex.Replace(name, @"^第\d+回\s*", string.Empty, RegexOptions.CultureInvariant).Trim();
        name = Regex.Replace(name, @"\s+G[ⅠⅡⅢ1-3]$", string.Empty, RegexOptions.CultureInvariant).Trim();

        // グレード表記（GⅠ等）が混入している場合はスペースで分割して先頭部分を使う
        var parts = name.Split([' ', '\u3000', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? string.Join(" ", parts) : name;
    }

    // ------------------------------------------------------------------ //
    // 出走馬エントリの解析
    // ------------------------------------------------------------------ //

    private static IReadOnlyList<JraRaceEntryData> ParseEntries(IReadOnlyList<PageTableSnapshot> tables)
    {
        var raceTable = tables.FirstOrDefault(IsRaceCardTable);
        return raceTable is null ? [] : ParseRaceCardTable(raceTable);
    }

    private static bool IsRaceCardTable(PageTableSnapshot table) =>
        table.Headers.Any(h =>
            RaceCardRequiredHeaders.Any(candidate =>
                NormalizeHeader(h).Contains(NormalizeHeader(candidate), StringComparison.OrdinalIgnoreCase)));

    private static IReadOnlyList<JraRaceEntryData> ParseRaceCardTable(PageTableSnapshot table)
    {
        var headers = table.Headers;

        var horseNameIndex = FindHeaderIndex(headers, "馬名", "競走馬");
        if (horseNameIndex < 0)
        {
            return [];
        }

        var horseNumberIndex = FindHeaderIndex(headers, "馬番");

        var gateNumberIndex = FindHeaderIndex(headers, "枠番", "枠");
        var jockeyIndex = FindHeaderIndex(headers, "騎手");
        var weightIndex = FindHeaderIndex(headers, "斤量", "負担重量");
        var sexAgeIndex = FindHeaderIndex(headers, "性齢");
        var bodyWeightIndex = FindHeaderIndex(headers, "馬体重");
        var trainerIndex = FindHeaderIndex(headers, "厩舎", "調教師");
        var ownerIndex = FindHeaderIndex(headers, "馬主");
        var breederIndex = FindHeaderIndex(headers, "生産者", "生産牧場");

        // JRA の出馬表は複合列ヘッダを持つことがある。
        // horseNameIndex と同一列になった場合は -1 に下げてセル内容から個別に抽出する。
        if (trainerIndex == horseNameIndex) trainerIndex = -1;
        if (bodyWeightIndex == horseNameIndex) bodyWeightIndex = -1;
        if (ownerIndex == horseNameIndex) ownerIndex = -1;
        if (breederIndex == horseNameIndex) breederIndex = -1;
        if (weightIndex == horseNameIndex) weightIndex = -1;
        if (sexAgeIndex == horseNameIndex) sexAgeIndex = -1;

        // 騎手列が複合列 (性齢/毛色 負担重量 騎手名) かどうか判定
        var jockeyCellIsCombined = jockeyIndex >= 0
            && (headers[jockeyIndex].Contains("性齢", StringComparison.OrdinalIgnoreCase)
                || headers[jockeyIndex].Contains("負担重量", StringComparison.OrdinalIgnoreCase));

        // 複合騎手列が個別列の代わりになっている場合はそちらも -1 に下げて複合列で解決する
        if (jockeyCellIsCombined)
        {
            if (sexAgeIndex < 0 || sexAgeIndex == jockeyIndex) sexAgeIndex = jockeyIndex;
            if (weightIndex < 0 || weightIndex == jockeyIndex) weightIndex = jockeyIndex;
        }

        var entries = new List<JraRaceEntryData>();
        foreach (var row in table.Rows)
        {
            if (row.Count == 0)
            {
                continue;
            }

            if (IsHeaderRow(row, headers))
            {
                continue;
            }

            var horseCellText = GetCell(row, horseNameIndex)?.Trim();
            if (string.IsNullOrWhiteSpace(horseCellText))
            {
                continue;
            }

            var horseNumberStr = GetCell(row, horseNumberIndex);
            var horseNumber = ParseInt(horseNumberStr);
            var gateNumber = ParseInt(GetCell(row, gateNumberIndex));
            if (horseNumber is null or <= 0)
            {
                horseNumber = ResolveProvisionalHorseNumber(row, headers, horseCellText, gateNumber, entries.Count);
                if (horseNumber is null or <= 0)
                {
                    continue;
                }
            }

            // 複合列から個別フィールドを抽出
            var horseName = ExtractHorseName(horseCellText);
            var (extractedOwnerName, extractedBreederName) = ExtractOwnerAndBreederFromHorseCell(horseCellText);
            var trainerName = trainerIndex >= 0
                ? NullIfEmpty(GetCell(row, trainerIndex))
                : ExtractTrainerFromHorseCell(horseCellText);
            var ownerName = ownerIndex >= 0
                ? NullIfEmpty(GetCell(row, ownerIndex))
                : extractedOwnerName;
            var breederName = breederIndex >= 0
                ? NullIfEmpty(GetCell(row, breederIndex))
                : extractedBreederName;

            var jockeyCellText = GetCell(row, jockeyIndex)?.Trim();
            string? jockeyName;
            string? sexAge;
            decimal? weight;
            if (jockeyCellIsCombined && jockeyCellText is not null)
            {
                jockeyName = ExtractJockeyFromCombinedCell(jockeyCellText);
                sexAge = NullIfEmpty(ExtractSexAgeFromCombinedCell(jockeyCellText));
                weight = ExtractWeightFromCombinedCell(jockeyCellText);
            }
            else
            {
                jockeyName = NullIfEmpty(jockeyCellText);
                sexAge = NullIfEmpty(GetCell(row, sexAgeIndex));
                weight = ParseDecimal(GetCell(row, weightIndex));
            }

            var bodyWeightCell = GetCell(row, bodyWeightIndex);
            if (string.IsNullOrWhiteSpace(bodyWeightCell))
            {
                bodyWeightCell = ExtractBodyWeightTextFromHorseCell(horseCellText);
            }

            var (bodyWeight, bodyWeightDiff) = ParseBodyWeight(bodyWeightCell);

            entries.Add(new JraRaceEntryData(
                HorseNumber: horseNumber.Value,
                GateNumber: gateNumber,
                HorseName: horseName,
                JockeyName: jockeyName,
                Weight: weight,
                SexAge: sexAge,
                BodyWeight: bodyWeight,
                BodyWeightDiff: bodyWeightDiff,
                TrainerName: trainerName,
                OwnerName: ownerName,
                BreederName: breederName));
        }

        return entries;
    }

    private static bool IsHeaderRow(IReadOnlyList<string> row, IReadOnlyList<string> headers)
    {
        if (row.Count != headers.Count)
        {
            return false;
        }

        for (var i = 0; i < row.Count; i++)
        {
            if (!string.Equals(row[i]?.Trim(), headers[i]?.Trim(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int? ResolveProvisionalHorseNumber(
        IReadOnlyList<string> row,
        IReadOnlyList<string> headers,
        string horseCellText,
        int? gateNumber,
        int existingEntryCount)
    {
        if (gateNumber.HasValue)
        {
            return null;
        }

        if (FindHeaderIndex(headers, "馬番") < 0)
        {
            return null;
        }

        if (!LooksLikeUndecidedRaceCardRow(row, horseCellText))
        {
            return null;
        }

        return existingEntryCount + 1;
    }

    private static bool LooksLikeUndecidedRaceCardRow(IReadOnlyList<string> row, string horseCellText)
    {
        var hasCombinedHorseProfile = horseCellText.Contains("父：", StringComparison.Ordinal)
            && ExtractTrainerFromHorseCell(horseCellText) is not null;
        if (!hasCombinedHorseProfile)
        {
            return false;
        }

        var leadingCells = row.Take(Math.Min(2, row.Count)).ToArray();
        return leadingCells.All(string.IsNullOrWhiteSpace)
            || leadingCells.Any(cell => string.Equals(cell?.Trim(), "ブリンカー着用", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ //
    // 複合セルからの個別フィールド抽出
    // ------------------------------------------------------------------ //

    // 馬名複合セル "デアトゥバトル (0.0.0.2) コウトミックレーシング..." → "デアトゥバトル"
    private static string ExtractHorseName(string cellText)
    {
        var first = cellText.TrimStart()
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return first ?? cellText.Trim();
    }

    // 馬名複合セルから調教師名を抽出: "水野 貴広(美浦)" → "水野 貴広"
    // JRA の出馬表では厩舎所属が (美浦)/(栗東)/(地方) の形で括弧内に入る
    private static readonly Regex TrainerInHorseCellRegex =
        new(@"([\p{L}]+\s+[\p{L}]+|[\p{L}]+)\((?:美浦|栗東|地方|JRA)\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BodyWeightInHorseCellRegex =
        new(@"(?<bodyWeight>\d{3,4}\s*kg\s*\((?:[-+]\d+|初出走)\))",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex PrizeSectionRegex =
        new(@"(?<label>(?:本賞金|付加賞金|付加賞|特別出走手当)(?:（[^）]+）)?)\s*(?<body>.*?)(?=(?:本賞金|付加賞金|付加賞|特別出走手当)(?:（[^）]+）)?|印刷用ページ|馬柱の見方|枠\s*馬番|$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PrizeEntryRegex =
        new(@"(?<place>[1-5])着\s*(?<amount>\d{1,3}(?:,\d{3})*(?:\.\d+)?|\d+(?:\.\d+)?)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] BreederKeywords =
    [
        "牧場",
        "ファーム",
        "スタッド",
        "スタツド",
        "ホースランチ",
        "Farm",
        "Ranch",
        "Stud",
        "Bloodstock",
        "Ecurie",
        "Thoroughbred"
    ];

    private static string? ExtractTrainerFromHorseCell(string? cellText)
    {
        if (string.IsNullOrWhiteSpace(cellText)) return null;
        var m = TrainerInHorseCellRegex.Match(cellText);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractBodyWeightTextFromHorseCell(string? cellText)
    {
        if (string.IsNullOrWhiteSpace(cellText))
        {
            return null;
        }

        var match = BodyWeightInHorseCellRegex.Match(cellText);
        return match.Success ? match.Groups["bodyWeight"].Value : null;
    }

    private static (string? OwnerName, string? BreederName) ExtractOwnerAndBreederFromHorseCell(string? cellText)
    {
        if (string.IsNullOrWhiteSpace(cellText))
        {
            return (null, null);
        }

        var ownerSegmentStart = 0;
        var bodyWeightMatch = BodyWeightInHorseCellRegex.Match(cellText);
        if (bodyWeightMatch.Success)
        {
            ownerSegmentStart = bodyWeightMatch.Index + bodyWeightMatch.Length;
        }

        var trainerMatch = TrainerInHorseCellRegex.Match(cellText);
        var ownerSegmentEnd = trainerMatch.Success ? trainerMatch.Index : cellText.Length;
        if (ownerSegmentEnd <= ownerSegmentStart)
        {
            return (null, null);
        }

        var segment = cellText[ownerSegmentStart..ownerSegmentEnd];
        var pedigreeIndex = segment.IndexOf("父：", StringComparison.Ordinal);
        if (pedigreeIndex >= 0)
        {
            segment = segment[..pedigreeIndex];
        }

        segment = segment.Trim();
        if (string.IsNullOrWhiteSpace(segment))
        {
            return (null, null);
        }

        var horseName = ExtractHorseName(cellText);
        if (string.Equals(segment, horseName, StringComparison.Ordinal))
        {
            return (null, null);
        }

        var breederIndex = FindBreederBoundary(segment);
        if (breederIndex > 0)
        {
            return (NullIfEmpty(segment[..breederIndex]), NullIfEmpty(segment[breederIndex..]));
        }

        var tokens = segment
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        if (tokens.Length == 0)
        {
            return (null, null);
        }

        if (tokens.Length == 1)
        {
            return (tokens[0], null);
        }

        var breederTokenCount = tokens.Length >= 3 ? 2 : 1;
        var ownerName = string.Join(" ", tokens[..^breederTokenCount]);
        var breederName = string.Join(" ", tokens[^breederTokenCount..]);
        return (NullIfEmpty(ownerName), NullIfEmpty(breederName));
    }

    private static int FindBreederBoundary(string text)
    {
        var boundary = -1;
        foreach (var keyword in BreederKeywords)
        {
            var index = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            // キーワード自体は生産者名の途中に出るので、直前の区切りまで戻す。
            var start = index;
            while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            {
                start--;
            }

            boundary = boundary < 0 ? start : Math.Min(boundary, start);
        }

        return boundary;
    }

    // 複合騎手セル "牡3/黒鹿 57.0kg 松若 風馬" → "松若 風馬"
    private static string? ExtractJockeyFromCombinedCell(string? cellText)
    {
        if (string.IsNullOrWhiteSpace(cellText)) return null;
        // "XXXkg " の後の文字列が騎手名
        var m = Regex.Match(cellText, @"\d+\.?\d*\s*kg\s+(.+)$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    // 複合騎手セル "牡3/黒鹿 57.0kg ..." → "牡3"
    private static string? ExtractSexAgeFromCombinedCell(string? cellText)
    {
        if (string.IsNullOrWhiteSpace(cellText)) return null;
        var m = Regex.Match(cellText, @"^([牡牝騸セ]\d+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    // 複合騎手セル "牡3/黒鹿 57.0kg ..." → 57.0
    private static decimal? ExtractWeightFromCombinedCell(string? cellText)
    {
        if (string.IsNullOrWhiteSpace(cellText)) return null;
        var m = Regex.Match(cellText, @"(\d+\.?\d*)\s*kg", RegexOptions.IgnoreCase);
        return m.Success
            && decimal.TryParse(m.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var w)
            ? w : null;
    }

    private static int FindHeaderIndex(IReadOnlyList<string> headers, params string[] candidates)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var normalizedHeader = NormalizeHeader(headers[i]);
            if (candidates.Any(c => normalizedHeader.Contains(NormalizeHeader(c), StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return -1;
    }

    private static string NormalizeHeader(string? value)
        => (value ?? string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u3000", string.Empty, StringComparison.Ordinal);

    private static string? GetCell(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index] : null;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value
            .Where(c => char.IsDigit(c) || c is '.' or '-' or '+')
            .ToArray());
        return decimal.TryParse(
            normalized,
            NumberStyles.Number | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static (decimal? weight, decimal? diff) ParseBodyWeight(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var trimmed = value.Trim();
        var open = trimmed.IndexOf('(');
        var close = trimmed.IndexOf(')');
        if (open > 0 && close > open)
        {
            var weight = ParseDecimal(trimmed[..open]);
            var diff = ParseDecimal(trimmed[(open + 1)..close]);
            return (weight, diff);
        }

        return (ParseDecimal(trimmed), null);
    }

    // ------------------------------------------------------------------ //
    // 内部レコード
    // ------------------------------------------------------------------ //

    private sealed record RaceMetadata(
        string RaceName,
        string? Racecourse,
        DateOnly? RaceDate,
        int? RaceNumber,
        int? MeetingNumber,
        int? DayNumber,
        TimeOnly? PostTime,
        string? ConditionSummary,
        string? AgeCondition,
        string? AgeConditionCode,
        string? RaceClass,
        string? RaceClassCode,
        string? Eligibility,
        IReadOnlyList<string> EligibilityCodes,
        string? EntryCondition,
        IReadOnlyList<string> EntryConditionCodes,
        string? WeightCondition,
        string? WeightConditionCode,
        string? CourseType,
        string? TrackDirection,
        int? Distance,
        string? Grade,
        IReadOnlyList<JraRacePrizeData> PrizeMoney);

    private sealed record MeetingInfo(
        int MeetingNumber,
        string Racecourse,
        int DayNumber);
}
