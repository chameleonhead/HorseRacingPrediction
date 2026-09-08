using System.Text.RegularExpressions;
using SemanticPageSnapshot = HorseRacingPrediction.Scraping.Browser.Snapshots.PageSnapshot;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

/// <summary>
/// 特定レースの結果ページを解析する。着順・馬番・馬名列を持つテーブルを対象とする。
/// 実ページの具体的なURL構造は未調査のため、テーブルの見出し（ヘッダー）から
/// 列を特定する方式にして、ページ構造変更への耐性を優先している。
/// </summary>
public sealed class RaceResultPageParser
    : IJraPageParser
{
    // 実運用で、払戻金額欄が半角数字ではなく全角数字（例: "０"）で出力される
    // ケースが確認された。char.IsDigit は全角数字も真を返すため一見数字文字列に
    // 見えるが、decimal.Parse/TryParse は全角数字を受け付けず解析に失敗する
    // （依頼書28節のエラーとして誤検知されてしまう）。数値解析の直前に全角数字を
    // 半角に正規化することで、実際に数値でない文字列だけを解析失敗として扱う。
    private static string NormalizeDigits(string value)
    {
        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            buffer[i] = c is >= '０' and <= '９' ? (char)(c - '０' + '0') : c;
        }

        return new string(buffer);
    }

    private static readonly Regex DateRegex =
        new(@"(?<year>\d{4})年\s*(?<month>\d{1,2})月\s*(?<day>\d{1,2})日", RegexOptions.Compiled);

    // Task16実サイト確認で判明: 実ページの見出しは「1レース」のように「R」ではなく
    // 「レース」表記であり、旧正規表現（digit+"R"）は常にマッチしなかった。
    private static readonly Regex RaceNumberRegex =
        new(@"(?<num>\d{1,2})\s*(?:R|レース)", RegexOptions.Compiled);

    private static readonly Regex LeadingNumberRegex =
        new(@"(?<num>\d{1,2})", RegexOptions.Compiled);

    private static readonly Regex TimeSpanRegex =
        new(@"(?:(?<min>\d{1,2}):)?(?<sec>\d{1,2})\.(?<frac>\d{1})", RegexOptions.Compiled);

    // 天候/馬場は実ページのHTML構造が未調査のため、見出し・本文中のテキストパターンから
    // 緩やかに抽出する方式にしている。ラベル自体が存在しない場合は「天候・馬場欄なし」の
    // 正常系（null）として扱うが、ラベルは存在するのに続く値がJRAの既知値集合に無い場合は
    // 「未知の値」（依頼書10節・11節）としてJraUnexpectedValueExceptionにする。
    private static readonly Regex WeatherLabelRegex =
        new(@"天候\s*[:：]?\s*(?<value>\S+)", RegexOptions.Compiled);

    private static readonly string[] KnownWeatherValues = ["晴", "曇", "小雨", "雨", "小雪", "雪"];

    // 実サイト確認（2026-09-07）で判明: 実ページの馬場状態表記は「天候 雨 芝 稍重 ダート 重」
    // のように「馬場」「馬場状態」という語を伴わず、「芝」「ダート」の直後に状態値が
    // 続くのみ。旧正規表現は「馬場」の語を必須としていたため常にマッチしなかった。
    private static readonly Regex TurfConditionLabelRegex =
        new(@"芝\s*(?<value>\S+)", RegexOptions.Compiled);

    private static readonly Regex DirtConditionLabelRegex =
        new(@"ダート\s*(?<value>\S+)", RegexOptions.Compiled);

    private static readonly string[] KnownTrackConditionValues = ["良", "稍重", "重", "不良"];

    // コース表記（依頼書12節）。依頼書に例示された実際のJRA表記
    // 「1,600メートル（芝・左）」「1,400メートル（ダート・左）」「2,890メートル（芝 外内）」
    // 「3,000メートル（芝→ダート）」のみを確実にサポートする。ページ上にこの形式の
    // 文字列自体が見つからない場合は「コース構造欄なし」の正常系（null）として扱うが、
    // 見つかったのに既知の表記へ分解できない場合はエラーとする。
    private static readonly Regex CourseSpecRegex =
        new(@"(?<distance>[\d,]+)\s*メートル\s*[（(](?<layout>[^）)]+)[）)]", RegexOptions.Compiled);

    public JraPageKind Kind =>
        JraPageKind.RaceResult;

    public int Priority => 85;

    public bool CanParse(
        SemanticPageSnapshot source)
    {
        var snapshot = JraSnapshotView.Create(source);
        return FindResultTable(snapshot) is not null;
    }

    public IJraPage Parse(
        SemanticPageSnapshot source)
    {
        var snapshot = JraSnapshotView.Create(source);
        var table =
            FindResultTable(snapshot)
            ?? throw new JraPageParseException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "レース結果テーブルを取得できませんでした。");

        var date =
            ParseDate(snapshot);

        var course =
            ParseCourse(snapshot);

        var number =
            ParseRaceNumber(snapshot);

        var raceName =
            ParseRaceName(snapshot);

        var results =
            ParseResults(table, snapshot.Url);

        // 依頼書29節: RaceResult全体Validationとして「結果行が1件以上存在する」ことを
        // 必須とする。着順テーブル自体は見つかったが結果行が0件の場合、成績なしの
        // レース結果ページとして正常扱いにはせず、Parser異常として検知する。
        if (results.Count == 0)
        {
            throw new JraPageStructureException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "レース結果テーブルに結果行が1件も存在しませんでした。",
                "Results");
        }

        // 依頼書14・29節: HorseNumberはレース内で一意であること。
        var duplicateHorseNumbers = results
            .GroupBy(r => r.HorseNumber)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateHorseNumbers.Count > 0)
        {
            throw new JraResultConsistencyException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "HorseNumberがレース内で重複しています。",
                "HorseNumber",
                string.Join(",", duplicateHorseNumbers));
        }

        var weatherText =
            ParseWeatherText(snapshot);

        var trackConditionText =
            ParseTrackConditionText(snapshot);

        var payouts =
            ParsePayouts(snapshot, table);

        var courseSpec =
            ParseCourseSpec(snapshot, raceName);

        var cornerPassages =
            ParseCornerPassages(snapshot);

        var overallPaceText =
            ParseOverallPaceText(snapshot);

        var prizeMoneyByPosition =
            ParsePrizeMoney(snapshot);

        return new JraRaceResultPage(
            snapshot.Url,
            new RaceId(date, course, number),
            raceName,
            results,
            weatherText,
            trackConditionText,
            payouts is not null && !payouts.IsEmpty ? payouts : null,
            courseSpec,
            cornerPassages is { Count: > 0 } ? cornerPassages : null,
            overallPaceText,
            prizeMoneyByPosition, RaceGrade.Parse(snapshot), RaceCardPageParser.ParseStartTime(snapshot, allowUnlabelledTime: false));
    }

    internal static RaceCourseSpec? ParseCourseSpec(
        JraSnapshotView snapshot,
        string raceName)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)} {snapshot.MainText}";

        var match = CourseSpecRegex.Match(searchText);

        if (!match.Success)
        {
            // 「メートル（…）」形式のコース表記自体が見つからない＝
            // 仕様上optionalな要素が存在しない正常系。
            return null;
        }

        var distanceDigits = match.Groups["distance"].Value.Replace(",", string.Empty);

        if (!int.TryParse(distanceDigits, out var distance) || distance <= 0)
        {
            throw new JraValueParseException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "Course.DistanceMeters",
                match.Groups["distance"].Value);
        }

        var rawLayout = match.Groups["layout"].Value.Trim();
        var raceType = raceName.Contains("障害", StringComparison.Ordinal) ? RaceType.Jump : RaceType.Flat;

        var surfaceTokens = rawLayout.Split('→', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (surfaceTokens.Length == 0)
        {
            throw new JraUnexpectedValueException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "Course.Layout",
                rawLayout);
        }

        CourseDirection? direction = null;
        string? layout = null;
        var surfaces = new List<CourseSurface>();

        for (var i = 0; i < surfaceTokens.Length; i++)
        {
            var token = surfaceTokens[i];

            // 最後のトークンのみ「芝・左」「芝 外内」のように方向・レイアウト注記を
            // 伴い得る（依頼書に例示された表記はいずれも末尾トークンにのみ注記を持つ）。
            string surfaceText;
            string? suffix = null;

            var directionDelimiterIndex = token.IndexOf('・');
            var spaceDelimiterIndex = token.IndexOf(' ');

            if (directionDelimiterIndex >= 0)
            {
                surfaceText = token[..directionDelimiterIndex];
                suffix = token[(directionDelimiterIndex + 1)..];
            }
            else if (spaceDelimiterIndex >= 0)
            {
                surfaceText = token[..spaceDelimiterIndex];
                suffix = token[(spaceDelimiterIndex + 1)..];
            }
            else
            {
                surfaceText = token;
            }

            var surface = surfaceText switch
            {
                "芝" => CourseSurface.Turf,
                "ダート" => CourseSurface.Dirt,
                _ => (CourseSurface?)null,
            };

            if (surface is null)
            {
                throw new JraUnexpectedValueException(
                    JraPageKind.RaceResult,
                    snapshot.Url,
                    "Course.Surface",
                    rawLayout);
            }

            surfaces.Add(surface.Value);

            if (suffix is not null)
            {
                if (directionDelimiterIndex >= 0)
                {
                    // 「・」区切りは方向表記であることが既知（依頼書例示の「芝・左」）。
                    // ただし実サイトには「芝・右 外」のように、方向の後ろへ半角スペース区切りで
                    // 外回り／内回り等のレイアウト注記が続く表記が存在する（Task: 実サイトE2E
                    // で判明、RawValue="芝・右 外"）。方向トークン（先頭の空白区切り要素）を
                    // 「左」「右」として解釈し、続きがあればレイアウト注記として保持する。
                    // 左右いずれでもない場合は未知の方向表記としてエラー。
                    var directionSpaceIndex = suffix.IndexOf(' ');

                    var directionToken =
                        directionSpaceIndex >= 0 ? suffix[..directionSpaceIndex] : suffix;

                    var layoutSuffix =
                        directionSpaceIndex >= 0 ? suffix[(directionSpaceIndex + 1)..].Trim() : null;

                    if (directionToken == "左")
                    {
                        direction = CourseDirection.Left;
                    }
                    else if (directionToken == "右")
                    {
                        direction = CourseDirection.Right;
                    }
                    else
                    {
                        throw new JraUnexpectedValueException(
                            JraPageKind.RaceResult,
                            snapshot.Url,
                            "Course.Direction",
                            rawLayout);
                    }

                    if (!string.IsNullOrEmpty(layoutSuffix))
                    {
                        layout = layout is null ? layoutSuffix : $"{layout} {layoutSuffix}";
                    }
                }
                else
                {
                    layout = suffix;
                }
            }
        }

        return new RaceCourseSpec(distance, raceType, surfaces, direction, layout, rawLayout);
    }

    // コーナー通過順位（依頼書23節）。コーナー数を固定せず可変長として扱う。
    // 実ページのヘッダー文字列は本セッションでは確認できていないが、「コーナー」を
    // 含む見出しは既知のJRA用語であるため、その列見出しから通過順位を抽出する。
    // 見つからない場合はコーナー通過順位欄自体が存在しない正常系として扱う。
    private static readonly Regex CornerNumberRegex =
        new(@"(?<num>\d+)\s*コーナー", RegexOptions.Compiled);

    private static IReadOnlyList<CornerPassage> ParseCornerPassages(
        JraSnapshotView snapshot)
    {
        var result = new List<CornerPassage>();

        foreach (var table in snapshot.Tables)
        {
            // レイアウト1: コーナー番号が列見出し（ヘッダー）として現れるテーブル
            // （既存Fixtureが前提とする構造）。
            var headerLayoutMatched = false;

            // tbodyの先頭thもHeadersへ投影されるため、行ラベルがあればそちらを優先する。
            var hasRowLabels = table.Rows.Any(row => row.Count >= 2 && CornerNumberRegex.IsMatch(row[0]));
            for (var i = 0; !hasRowLabels && i < table.Headers.Count; i++)
            {
                var header = table.Headers[i];

                if (!header.Contains("コーナー", StringComparison.Ordinal))
                {
                    continue;
                }

                var cornerNumberMatch = CornerNumberRegex.Match(header);

                if (!cornerNumberMatch.Success)
                {
                    // コーナー番号自体を特定できない見出しは、通過順位欄ではない
                    // 別の用途の可能性があるため読み飛ばす（構造は既知集合外だが
                    // 必須要素ではないため無視して継続する）。
                    continue;
                }

                headerLayoutMatched = true;
                var cornerNumber = int.Parse(cornerNumberMatch.Groups["num"].Value);

                foreach (var row in table.Rows)
                {
                    if (i >= row.Count || string.IsNullOrWhiteSpace(row[i]))
                    {
                        continue;
                    }

                    result.Add(new CornerPassage(cornerNumber, row[i].Trim()));
                }
            }

            if (headerLayoutMatched)
            {
                continue;
            }

            // レイアウト2（実サイトで確認）: 「1コーナー」「2コーナー(2周目)」等が
            // 列見出しではなく、行の1列目（ラベルセル）として現れ、2列目以降に
            // 通過順位の生文字列が続く2列テーブル。
            foreach (var row in table.Rows)
            {
                if (row.Count < 2 || string.IsNullOrWhiteSpace(row[0]))
                {
                    continue;
                }

                var label = row[0].Trim();

                if (!label.Contains("コーナー", StringComparison.Ordinal))
                {
                    continue;
                }

                var cornerNumberMatch = CornerNumberRegex.Match(label);

                if (!cornerNumberMatch.Success)
                {
                    continue;
                }

                var cornerNumber = int.Parse(cornerNumberMatch.Groups["num"].Value);

                for (var i = 1; i < row.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(row[i]))
                    {
                        continue;
                    }

                    result.Add(new CornerPassage(cornerNumber, row[i].Trim()));
                }
            }
        }

        return result;
    }

    // Phase8（依頼書27節）: 本賞金(万円)。「1着 840　2着 340…」のように、
    // 着順ラベルと万円単位の金額が繰り返し現れる。円単位に変換して保持する
    // （既存のPayoutLine.Amountとの単位整合を優先）。「本賞金」欄自体が存在
    // しない場合は正常（null）。存在するのに1件も解析できない場合はエラー。
    private static readonly Regex PrizeMoneySectionRegex =
        new(@"本賞金[^\d]*", RegexOptions.Compiled);

    private static readonly Regex PrizeMoneyEntryRegex =
        new(@"(?<position>\d{1,2})着\s*(?<amount>[\d,]+)", RegexOptions.Compiled);

    private static IReadOnlyDictionary<int, decimal>? ParsePrizeMoney(
        JraSnapshotView snapshot)
    {
        var searchText = $"{string.Join(" ", snapshot.Headings)} {snapshot.MainText}";

        var sectionMatch = PrizeMoneySectionRegex.Match(searchText);

        if (!sectionMatch.Success)
        {
            // 「本賞金」欄自体が存在しない＝仕様上optionalな要素が存在しない正常系。
            return null;
        }

        var tail = searchText[(sectionMatch.Index + sectionMatch.Length)..];

        // 「本賞金」ラベル自体は見つかったが、その直後にラベル+金額の並びが
        // 1件も解析できない場合はParser異常（依頼書27節）。
        var truncated = tail.Length > 200 ? tail[..200] : tail;

        var entries = PrizeMoneyEntryRegex.Matches(truncated);

        if (entries.Count == 0)
        {
            throw new JraValueParseException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "PrizeMoneyByPosition",
                truncated);
        }

        var result = new Dictionary<int, decimal>();

        foreach (Match entry in entries)
        {
            var position = int.Parse(entry.Groups["position"].Value);
            var amountDigits = entry.Groups["amount"].Value.Replace(",", string.Empty);

            if (!decimal.TryParse(amountDigits, out var manEn))
            {
                throw new JraValueParseException(
                    JraPageKind.RaceResult,
                    snapshot.Url,
                    "PrizeMoneyByPosition",
                    entry.Value);
            }

            // 万円単位→円単位。
            result[position] = manEn * 10_000m;
        }

        return result;
    }

    // Phase8: レース全体の「タイム」欄（上り集計）。フォーマット解析難易度が
    // 高いため、数値分解はせず「上り」ラベル以降の生文字列のみ保持する
    // （できれば対応、依頼書34節フォローアップ）。「上り」ラベル自体が
    // 存在しない場合は正常（null）。
    private static readonly Regex OverallPaceRegex =
        new(@"上り[:：]?\s*(?<val>\d[^\r\n]{0,79})", RegexOptions.Compiled);

    private static string? ParseOverallPaceText(
        JraSnapshotView snapshot)
    {
        var labelledRows = snapshot.Tables.SelectMany(table => table.Rows)
            .Where(row => row.Count >= 2 && row[0].Trim() is "ハロンタイム" or "上り")
            .Where(row => row.Skip(1).Any(value => !string.IsNullOrWhiteSpace(value)))
            .Select(row => $"{row[0].Trim()}: {string.Join(" ", row.Skip(1)).Trim()}")
            .Distinct().ToList();
        if (labelledRows.Count > 0) return string.Join(" / ", labelledRows);

        var match = OverallPaceRegex.Match(snapshot.MainText);

        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["val"].Value;

        // 次のセクションラベルが混入していれば、そこで打ち切る。
        foreach (var stopWord in new[] { "本賞金", "コーナー通過順位", "払戻金" })
        {
            var stopIndex = value.IndexOf(stopWord, StringComparison.Ordinal);

            if (stopIndex >= 0)
            {
                value = value[..stopIndex];
            }
        }

        value = value.Trim();

        return value.Length == 0 ? null : value;
    }

    private static string? ParseWeatherText(
        JraSnapshotView snapshot)
    {
        var searchText =
            $"{string.Join(" ", snapshot.Headings)} {snapshot.MainText}";

        var match =
            WeatherLabelRegex.Match(searchText);

        if (!match.Success)
        {
            // 「天候」ラベル自体が存在しない＝仕様上optionalな要素が存在しない正常系。
            return null;
        }

        var value = match.Groups["value"].Value;

        if (!KnownWeatherValues.Contains(value, StringComparer.Ordinal))
        {
            throw new JraUnexpectedValueException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "Weather",
                value);
        }

        return value;
    }

    private static string? ParseTrackConditionText(
        JraSnapshotView snapshot)
    {
        var searchText =
            $"{string.Join(" ", snapshot.Headings)} {snapshot.MainText}";

        var turfMatch = TurfConditionLabelRegex.Match(searchText);
        var dirtMatch = DirtConditionLabelRegex.Match(searchText);

        if (!turfMatch.Success && !dirtMatch.Success)
        {
            return null;
        }

        var parts = new List<string>();
        if (turfMatch.Success)
        {
            var value = turfMatch.Groups["value"].Value;

            if (!KnownTrackConditionValues.Contains(value, StringComparer.Ordinal))
            {
                throw new JraUnexpectedValueException(
                    JraPageKind.RaceResult,
                    snapshot.Url,
                    "TrackCondition(Turf)",
                    value);
            }

            parts.Add($"芝:{value}");
        }

        if (dirtMatch.Success)
        {
            var value = dirtMatch.Groups["value"].Value;

            if (!KnownTrackConditionValues.Contains(value, StringComparer.Ordinal))
            {
                throw new JraUnexpectedValueException(
                    JraPageKind.RaceResult,
                    snapshot.Url,
                    "TrackCondition(Dirt)",
                    value);
            }

            parts.Add($"ダート:{value}");
        }

        return string.Join(" ", parts);
    }

    // Phase8: 券種キーワード→格納先バケット名の対応。「三連複」「三連単」（旧字体）
    // と「3連複」「3連単」（実サイトで確認された算用数字表記）の両方を許容する。
    private static readonly string[] KnownPayoutTypeNames =
        ["単勝", "複勝", "枠連", "馬連", "ワイド", "馬単", "三連複", "3連複", "三連単", "3連単"];

    private enum PayoutBucketKind
    {
        Win,
        Place,
        BracketQuinella,
        Quinella,
        Wide,
        Exacta,
        Trio,
        Trifecta,
    }

    private static PayoutBucketKind? ResolvePayoutBucketKind(
        string typeName)
        => typeName switch
        {
            "単勝" => PayoutBucketKind.Win,
            "複勝" => PayoutBucketKind.Place,
            "枠連" => PayoutBucketKind.BracketQuinella,
            "馬連" => PayoutBucketKind.Quinella,
            "ワイド" => PayoutBucketKind.Wide,
            "馬単" => PayoutBucketKind.Exacta,
            "三連複" or "3連複" => PayoutBucketKind.Trio,
            "三連単" or "3連単" => PayoutBucketKind.Trifecta,
            _ => null,
        };

    // Phase8: 券種の組合せ→払戻金→(人気)を繰り返し抽出するための正規表現。
    // 「2 → 400円 (3人気)」「2-7 → 810円 (4人気)」「2-6-7 → 1,160円 (5人気)」の
    // ように、矢印の有無・カッコの有無いずれにも対応する。
    private static readonly Regex PayoutEntryRegex =
        new(@"(?<combo>\d+(?:[-－]\d+){0,2})\s*(?:→\s*)?(?<amount>[\d,]+)\s*円\s*(?:[\(（]?\s*(?<pop>\d+)\s*人気\s*[\)）]?)?", RegexOptions.Compiled);

    private static RacePayouts? ParsePayouts(
        JraSnapshotView snapshot,
        JraTableView resultTable)
    {
        var buckets = new Dictionary<PayoutBucketKind, List<PayoutLine>>
        {
            [PayoutBucketKind.Win] = [],
            [PayoutBucketKind.Place] = [],
            [PayoutBucketKind.BracketQuinella] = [],
            [PayoutBucketKind.Quinella] = [],
            [PayoutBucketKind.Wide] = [],
            [PayoutBucketKind.Exacta] = [],
            [PayoutBucketKind.Trio] = [],
            [PayoutBucketKind.Trifecta] = [],
        };

        var populatedFromTable = new HashSet<PayoutBucketKind>();

        // 1. 既存方式: 「式別」「組合せ」「払戻金」の3列を持つ単純な1テーブル構造
        // （旧来のJRA表記、または本パーサーの既存Fixtureが前提とする構造）。
        foreach (var table in snapshot.Tables)
        {
            if (ReferenceEquals(table, resultTable))
            {
                continue;
            }

            var typeColumnIndex = FindPayoutTypeColumnIndex(table.Headers);
            var combinationColumnIndex = FindPayoutCombinationColumnIndex(table.Headers);
            var amountColumnIndex = FindPayoutAmountColumnIndex(table.Headers);

            if (combinationColumnIndex < 0 || amountColumnIndex < 0)
            {
                continue;
            }

            string? currentTypeName = null;

            foreach (var row in table.Rows)
            {
                if (typeColumnIndex >= 0 && typeColumnIndex < row.Count &&
                    !string.IsNullOrWhiteSpace(row[typeColumnIndex]))
                {
                    currentTypeName = row[typeColumnIndex].Trim();
                }

                if (currentTypeName is null ||
                    combinationColumnIndex >= row.Count ||
                    amountColumnIndex >= row.Count)
                {
                    continue;
                }

                var bucketKind = ResolvePayoutBucketKind(currentTypeName);

                if (bucketKind is null)
                {
                    // 券種らしきデータ（式別セルの値）があるのに既知の券種集合に
                    // 含まれない場合は黙って無視せずエラーとする（依頼書28節）。
                    // ただし式別セルが単なる空行・区切り行の可能性もあるため、
                    // 組合せ・金額のいずれかに実データがある場合のみエラー扱いにする。
                    if (!string.IsNullOrWhiteSpace(row[combinationColumnIndex]) ||
                        !string.IsNullOrWhiteSpace(row[amountColumnIndex]))
                    {
                        throw new JraUnexpectedValueException(
                            JraPageKind.RaceResult,
                            snapshot.Url,
                            "PayoutType",
                            currentTypeName);
                    }

                    continue;
                }

                AppendPayoutLines(buckets[bucketKind.Value], row[combinationColumnIndex], row[amountColumnIndex], snapshot.Url);
                populatedFromTable.Add(bucketKind.Value);
            }
        }

        // 2. 実サイトで確認された払戻金テーブルはグリッドレイアウト（複数券種ブロックが
        // 横並び）であり、単純な「式別/組合せ/払戻金」列構造への依存では壊れやすい。
        // そのため、テーブル構造から取得できなかった券種については、ページ本文
        // テキスト全体を対象に、券種キーワードをアンカーとした正規表現スキャンで
        // 補完する（既存の天候・馬場状態解析と同じ方式）。
        var searchText = BuildPayoutSearchText(snapshot, resultTable);

        var anchors = new List<(int Index, int Length, string TypeName)>();

        foreach (var typeName in KnownPayoutTypeNames)
        {
            var searchFrom = 0;

            while (true)
            {
                var index = searchText.IndexOf(typeName, searchFrom, StringComparison.Ordinal);

                if (index < 0)
                {
                    break;
                }

                anchors.Add((index, typeName.Length, typeName));
                searchFrom = index + typeName.Length;
            }
        }

        anchors.Sort((a, b) => a.Index.CompareTo(b.Index));

        for (var i = 0; i < anchors.Count; i++)
        {
            var bucketKind = ResolvePayoutBucketKind(anchors[i].TypeName);

            if (bucketKind is null || populatedFromTable.Contains(bucketKind.Value))
            {
                continue;
            }

            var blockStart = anchors[i].Index + anchors[i].Length;
            var blockEnd = i + 1 < anchors.Count ? anchors[i + 1].Index : searchText.Length;

            if (blockEnd <= blockStart)
            {
                continue;
            }

            var block = searchText[blockStart..blockEnd];
            var bucket = buckets[bucketKind.Value];

            foreach (Match entryMatch in PayoutEntryRegex.Matches(block))
            {
                var combination = NormalizeDigits(entryMatch.Groups["combo"].Value).Replace('－', '-');
                var amountDigits = NormalizeDigits(entryMatch.Groups["amount"].Value).Replace(",", string.Empty);

                if (!decimal.TryParse(amountDigits, out var amount))
                {
                    throw new JraValueParseException(
                        JraPageKind.RaceResult,
                        snapshot.Url,
                        "Payout.Amount",
                        entryMatch.Groups["amount"].Value);
                }

                int? popularity = entryMatch.Groups["pop"].Success
                    ? int.Parse(NormalizeDigits(entryMatch.Groups["pop"].Value))
                    : null;

                bucket.Add(new PayoutLine(combination, amount, popularity));
            }
        }

        return new RacePayouts(
            buckets[PayoutBucketKind.Win],
            buckets[PayoutBucketKind.Place],
            buckets[PayoutBucketKind.Quinella],
            buckets[PayoutBucketKind.Exacta],
            buckets[PayoutBucketKind.Trifecta],
            buckets[PayoutBucketKind.BracketQuinella],
            buckets[PayoutBucketKind.Wide],
            buckets[PayoutBucketKind.Trio]);
    }

    private static string BuildPayoutSearchText(
        JraSnapshotView snapshot,
        JraTableView resultTable)
    {
        var parts = new List<string> { snapshot.MainText };

        foreach (var table in snapshot.Tables)
        {
            if (ReferenceEquals(table, resultTable))
            {
                continue;
            }

            parts.Add(string.Join(" ", table.Headers));

            foreach (var row in table.Rows)
            {
                parts.Add(string.Join(" ", row));
            }
        }

        return string.Join(" ", parts);
    }

    private static void AppendPayoutLines(
        List<PayoutLine> bucket,
        string combinationCell,
        string amountCell,
        string url)
    {
        var combinations = SplitPayoutCellLines(combinationCell);
        var amounts = SplitPayoutCellLines(amountCell);

        for (var i = 0; i < combinations.Count; i++)
        {
            var amountText = i < amounts.Count ? amounts[i] : amounts.LastOrDefault();

            if (string.IsNullOrWhiteSpace(amountText))
            {
                continue;
            }

            var digitsOnly = new string(NormalizeDigits(amountText).Where(char.IsAsciiDigit).ToArray());

            if (digitsOnly.Length == 0 || !decimal.TryParse(digitsOnly, out var amount))
            {
                // 払戻値らしきセルが存在するのに数値として解析できない（依頼書28節）。
                throw new JraValueParseException(
                    JraPageKind.RaceResult,
                    url,
                    "Payout.Amount",
                    amountText);
            }

            bucket.Add(new PayoutLine(combinations[i], amount));
        }
    }

    private static List<string> SplitPayoutCellLines(string cell)
        => cell
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .ToList();

    private static int FindPayoutTypeColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("式別", StringComparison.Ordinal) ||
                headers[i].Contains("券種", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindPayoutCombinationColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (RemoveWhitespace(headers[i]).Contains("組合せ", StringComparison.Ordinal) ||
                RemoveWhitespace(headers[i]).Contains("馬番", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindPayoutAmountColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("払戻", StringComparison.Ordinal) ||
                headers[i].Contains("金額", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static JraTableView? FindResultTable(
        JraSnapshotView snapshot)
    {
        foreach (var table in snapshot.Tables)
        {
            if (FindFinishPositionColumnIndex(table.Headers) < 0)
            {
                continue;
            }

            if (FindHorseNumberColumnIndex(table.Headers) < 0)
            {
                continue;
            }

            if (FindHorseNameColumnIndex(table.Headers) < 0)
            {
                continue;
            }

            return table;
        }

        return null;
    }

    private static int FindFinishPositionColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("着順", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindHorseNumberColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            // Task16実サイト確認で判明: 実ページのヘッダーはセル内改行により
            // 「馬 番」のように空白入りで取得される。空白を除去して比較する。
            if (RemoveWhitespace(headers[i]).Contains("馬番", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string RemoveWhitespace(string value)
        => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));

    private static int FindHorseNameColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("馬名", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindJockeyColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("騎手", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindTimeColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("タイム", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    // 性齢列は「性齢」列見出しがあるページでのみ解析する（依頼書33節: 実サイトの
    // 列構成が本タスクの既存Fixtureでは未確認のため、列が存在しない場合は正常に
    // null/nullを返し、列が存在するのに値を解析できない場合のみエラーとする）。
    private static int FindSexAgeColumnIndex(
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (RemoveWhitespace(headers[i]).Contains("性齢", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static readonly Regex SexAgeRegex =
        new(@"^(?<sex>牡|牝|せん)(?<age>\d{1,2})$", RegexOptions.Compiled);

    // 以下、依頼書14・20・21・24・25・26節に対応する列。依頼書の記述に文字通り
    // 現れるJRA既知用語（枠番/斤量/調教師/人気/馬体重/着差/推定上り/平均1F）を
    // 見出しとして検出する。見出し自体が存在しない場合はその項目全体を欠損として
    // null扱いにする（正常系）。値が存在するのに解析できない場合のみエラーにする。
    private static int FindFrameNumberColumnIndex(IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (RemoveWhitespace(headers[i]).Contains("枠", StringComparison.Ordinal) || RemoveWhitespace(headers[i]).Contains("枠番", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindAssignedWeightColumnIndex(IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (RemoveWhitespace(headers[i]).Contains("負担重量", StringComparison.Ordinal) || headers[i].Contains("斤量", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindTrainerColumnIndex(IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("調教師", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindPopularityColumnIndex(IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("人気", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindBodyWeightColumnIndex(IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (RemoveWhitespace(headers[i]).Contains("馬体重", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindMarginColumnIndex(IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (headers[i].Contains("着差", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindEstimatedLast3FColumnIndex(IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (RemoveWhitespace(headers[i]).Contains("推定上り", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    // Phase8: 着順テーブル内の「コーナー通過順位」列（馬ごとのインライン列、
    // 例:「4 3 3 2」）。ページ下方の集計テーブル（ParseCornerPassages）とは別物。
    private static int FindCornerOrdersColumnIndex(IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (RemoveWhitespace(headers[i]).Contains("コーナー通過順位", StringComparison.Ordinal) ||
                RemoveWhitespace(headers[i]).Contains("通過順位", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindAverage1FColumnIndex(IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (RemoveWhitespace(headers[i]).Contains("平均1F", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static readonly Regex BodyWeightRegex =
        new(@"^(?<weight>\d{3})\s*\((?:(?<change>[+-]?\d+)|(?<debut>初出走))\)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 降着（依頼書18節）。着順欄に確定順位と元の入線順位が
    // 「10(1位降着)」のように併記されるケースを検出する。
    private static readonly Regex DemotionRegex =
        new(@"^(?<pos>\d+)\s*[（(](?<original>\d+)\s*位?\s*降着[）)]$", RegexOptions.Compiled);

    private static DateOnly ParseDate(
        JraSnapshotView snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)}";

        var match =
            DateRegex.Match(searchText);

        if (!match.Success)
        {
            throw new JraPageParseException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "対象日付を取得できませんでした。");
        }

        return new DateOnly(
            int.Parse(match.Groups["year"].Value),
            int.Parse(match.Groups["month"].Value),
            int.Parse(match.Groups["day"].Value));
    }

    private static RaceCourse ParseCourse(
        JraSnapshotView snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)}";

        var course =
            RaceCourseNames.Parse(searchText);

        if (course == RaceCourse.Unknown)
        {
            throw new JraPageParseException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "対象競馬場を取得できませんでした。");
        }

        return course;
    }

    private static int ParseRaceNumber(
        JraSnapshotView snapshot)
    {
        var searchText =
            $"{snapshot.Title} {string.Join(" ", snapshot.Headings)}";

        var match =
            RaceNumberRegex.Match(searchText);

        if (!match.Success)
        {
            throw new JraPageParseException(
                JraPageKind.RaceResult,
                snapshot.Url,
                "対象レース番号を取得できませんでした。");
        }

        return int.Parse(match.Groups["num"].Value);
    }

    // レース結果ページのタイトル見出し（サイト全体のマストヘッド「JRA 日本中央競馬会」等）を
    // 誤ってレース名として拾わないための除外リスト。
    private static readonly string[] NonRaceNameHeadings =
    [
        "JRA 日本中央競馬会",
        "払戻金",
        "勝馬の紹介",
        "JRAからのお知らせ",
        "Footer",
    ];

    /// <summary>
    /// 実サイト確認（2026-09-07）で判明: 見出し一覧の先頭は常にサイト全体の
    /// マストヘッド見出し「JRA 日本中央競馬会」であり、旧実装（「競馬場名でも日付でも
    /// ない最初の見出し」という緩い判定）はこれを誤ってレース名として採用していた
    /// （実運用で全レースのRaceNameが"JRA 日本中央競馬会"になる事象として確認）。
    /// 実際のレース名（例：「障害3歳以上未勝利」）は、日付・競馬場・レース番号が
    /// すべて含まれる見出し（例：「レース結果 2026年9月6日（日曜）4回中山2日 1レース」）の
    /// 直後に続くことを確認したため、その見出しを起点にレース名を探す。
    /// 成績ページでは毎回レース名が確定しているはずの情報のため、特定できない場合は
    /// 他の識別情報（日付・競馬場・レース番号）と同様に例外として扱う。
    /// </summary>
    private static string ParseRaceName(
        JraSnapshotView snapshot)
    {
        var headings = snapshot.Headings;
        var metaIndex = -1;
        for (var i = 0; i < headings.Count; i++)
        {
            if (DateRegex.IsMatch(headings[i]) && RaceNumberRegex.IsMatch(headings[i]))
            {
                metaIndex = i;
                break;
            }
        }

        if (metaIndex >= 0)
        {
            for (var i = metaIndex + 1; i < headings.Count; i++)
            {
                var candidate = headings[i].Trim();

                if (RaceNameHeading.IsFollowingSection(candidate)) break;

                if (string.IsNullOrWhiteSpace(candidate) ||
                    RaceNameHeading.IsMeeting(candidate) ||
                    DateRegex.IsMatch(candidate) ||
                    NonRaceNameHeadings.Contains(candidate, StringComparer.Ordinal))
                {
                    continue;
                }

                return candidate;
            }
        }

        throw new JraPageParseException(
            JraPageKind.RaceResult,
            snapshot.Url,
            "レース名を取得できませんでした。ページ構造が想定と異なる可能性があります。");
    }

    private static IReadOnlyList<RaceResultEntry> ParseResults(
        JraTableView table,
        string url)
    {
        var finishIndex = FindFinishPositionColumnIndex(table.Headers);
        var horseNumberIndex = FindHorseNumberColumnIndex(table.Headers);
        var frameNumberIndex = FindFrameNumberColumnIndex(table.Headers);
        var horseNameIndex = FindHorseNameColumnIndex(table.Headers);
        var sexAgeIndex = FindSexAgeColumnIndex(table.Headers);
        var assignedWeightIndex = FindAssignedWeightColumnIndex(table.Headers);
        var jockeyIndex = FindJockeyColumnIndex(table.Headers);
        var timeIndex = FindTimeColumnIndex(table.Headers);
        var marginIndex = FindMarginColumnIndex(table.Headers);
        var cornerOrdersIndex = FindCornerOrdersColumnIndex(table.Headers);
        var estimatedLast3FIndex = FindEstimatedLast3FColumnIndex(table.Headers);
        var average1FIndex = FindAverage1FColumnIndex(table.Headers);
        var bodyWeightIndex = FindBodyWeightColumnIndex(table.Headers);
        var trainerIndex = FindTrainerColumnIndex(table.Headers);
        var popularityIndex = FindPopularityColumnIndex(table.Headers);

        var results = new List<RaceResultEntry>();

        foreach (var row in table.Rows)
        {
            // Task16実サイト確認で判明: 抽出したテーブルの1行目にヘッダー行自体が
            // 重複して含まれることがある。見出し文字列をレース結果として扱わない
            // よう読み飛ばす。
            if (row.Count > 0 && table.Headers.Count > 0 &&
                string.Equals(row[0], table.Headers[0], StringComparison.Ordinal))
            {
                continue;
            }

            if (finishIndex >= row.Count ||
                horseNumberIndex >= row.Count ||
                horseNameIndex >= row.Count)
            {
                continue;
            }

            var finishText = row[finishIndex].Trim();

            // 数字着順以外に、JRAの正常な特殊状態（取消・除外・中止・失格）を
            // ResultStatusとしてモデル化する（依頼書16節）。従来はここで単純に
            // 行を読み飛ばしていたため、これらの馬が結果からサイレントに欠落していた。
            ResultStatus status;
            int? finishPosition;
            int? originalFinishPosition = null;

            // 降着（依頼書18節）。「10(1位降着)」のように確定順位と元の入線順位が
            // 併記される表記を最初に検出する。
            var demotionMatch = DemotionRegex.Match(finishText);

            if (demotionMatch.Success)
            {
                status = ResultStatus.Finished;
                finishPosition = int.Parse(demotionMatch.Groups["pos"].Value);
                originalFinishPosition = int.Parse(demotionMatch.Groups["original"].Value);
            }
            else if (finishText.Contains("降着", StringComparison.Ordinal))
            {
                // 降着表現を検出したのに元の入線順位を解析できない場合はエラー
                // （依頼書18節）。
                throw new JraResultConsistencyException(
                    JraPageKind.RaceResult,
                    url,
                    "降着を検出しましたが、元の入線順位を解析できませんでした。",
                    "OriginalFinishPosition",
                    finishText);
            }
            else if (LeadingNumberRegex.Match(finishText) is { Success: true } finishMatch)
            {
                status = ResultStatus.Finished;
                finishPosition = int.Parse(finishMatch.Groups["num"].Value);
            }
            else if (ResultStatusText.TryParse(finishText, out var specialStatus))
            {
                status = specialStatus;
                finishPosition = null;
            }
            else if (string.IsNullOrWhiteSpace(finishText))
            {
                // 着順欄が完全に空白の行は、罫線用の空行等パース対象外の行である
                // 可能性が高く、既存の挙動（読み飛ばし）を維持する。
                continue;
            }
            else
            {
                throw new JraUnexpectedValueException(
                    JraPageKind.RaceResult,
                    url,
                    "ResultStatus",
                    finishText);
            }

            var numberMatch =
                LeadingNumberRegex.Match(row[horseNumberIndex]);

            if (!numberMatch.Success)
            {
                // この時点でfinishTextから既にResultStatusを確定できており、
                // 見出し行・空行等の「そもそも結果行ではない行」は上のフィルタで
                // 既に除外済みのため、ここへ到達する行は正真正銘の結果行である。
                // 馬番セル自体は存在するのに数字として解析できない場合は、
                // 既知項目の形式不正（依頼書7・29節）としてエラーにする。
                throw new JraValueParseException(
                    JraPageKind.RaceResult,
                    url,
                    "HorseNumber",
                    row[horseNumberIndex]);
            }

            var horseNumber =
                int.Parse(numberMatch.Groups["num"].Value);

            var horseName = row[horseNameIndex];

            if (string.IsNullOrWhiteSpace(horseName))
            {
                // この時点でfinishText・馬番のいずれからも「正真正銘の結果行」
                // であることが確定済み（見出し行・空行等のフィルタは通過済み）。
                // 馬名列が存在するのに値が空/空白の場合は、依頼書14・29節が
                // 必須とするHorseNameの欠落であり、正常な欠損として黙って
                // 読み飛ばしてはならない（依頼書7節: 必須要素が存在しない→Error）。
                throw new JraValueParseException(
                    JraPageKind.RaceResult,
                    url,
                    "HorseName",
                    horseName);
            }

            var jockeyName =
                jockeyIndex >= 0 && jockeyIndex < row.Count && !string.IsNullOrWhiteSpace(row[jockeyIndex])
                    ? row[jockeyIndex]
                    : null;

            TimeSpan? time = null;

            if (timeIndex >= 0 && timeIndex < row.Count)
            {
                var timeMatch = TimeSpanRegex.Match(row[timeIndex]);

                if (timeMatch.Success)
                {
                    var minutes =
                        timeMatch.Groups["min"].Success
                            ? int.Parse(timeMatch.Groups["min"].Value)
                            : 0;

                    var seconds = int.Parse(timeMatch.Groups["sec"].Value);
                    var fractionTenths = int.Parse(timeMatch.Groups["frac"].Value);

                    time = new TimeSpan(0, 0, minutes, seconds, fractionTenths * 100);
                }
            }

            // Finishedであれば必須（依頼書19・29節）。値が存在するのに解析できない場合と
            // 完全に欠落している場合の両方をエラーにする（依頼書29節「Finished: Timeあり」）。
            if (status == ResultStatus.Finished && time is null)
            {
                if (timeIndex >= 0 && timeIndex < row.Count && !string.IsNullOrWhiteSpace(row[timeIndex]))
                {
                    throw new JraValueParseException(
                        JraPageKind.RaceResult,
                        url,
                        "Time",
                        row[timeIndex]);
                }

                throw new JraResultConsistencyException(
                    JraPageKind.RaceResult,
                    url,
                    $"ResultStatus=Finishedの馬番{horseNumber}にTimeが存在しません。",
                    "Time",
                    timeIndex >= 0 && timeIndex < row.Count ? row[timeIndex] : null);
            }

            // FinishPositionは構造上、Finished判定分岐で必ず設定されるが、
            // 依頼書29節が明示的に求める整合性チェックとして防御的に確認する。
            if (status == ResultStatus.Finished && finishPosition is null)
            {
                throw new JraResultConsistencyException(
                    JraPageKind.RaceResult,
                    url,
                    $"ResultStatus=Finishedの馬番{horseNumber}にFinishPositionが存在しません。",
                    "FinishPosition",
                    finishText);
            }

            HorseSex? sex = null;
            int? age = null;

            if (sexAgeIndex >= 0 && sexAgeIndex < row.Count && !string.IsNullOrWhiteSpace(row[sexAgeIndex]))
            {
                var sexAgeText = row[sexAgeIndex].Trim();
                var sexAgeMatch = SexAgeRegex.Match(sexAgeText);

                if (!sexAgeMatch.Success)
                {
                    throw new JraUnexpectedValueException(
                        JraPageKind.RaceResult,
                        url,
                        "Sex",
                        sexAgeText);
                }

                if (!HorseSexText.TryParse(sexAgeMatch.Groups["sex"].Value, out var parsedSex))
                {
                    throw new JraUnexpectedValueException(
                        JraPageKind.RaceResult,
                        url,
                        "Sex",
                        sexAgeText);
                }

                sex = parsedSex;
                age = int.Parse(sexAgeMatch.Groups["age"].Value);
            }

            int? frameNumber = null;

            if (frameNumberIndex >= 0 && frameNumberIndex < row.Count && !string.IsNullOrWhiteSpace(row[frameNumberIndex]))
            {
                var frameNumberText = row[frameNumberIndex].Trim();
                var frameNumberMatch = LeadingNumberRegex.Match(frameNumberText);

                if (!frameNumberMatch.Success)
                {
                    throw new JraValueParseException(
                        JraPageKind.RaceResult,
                        url,
                        "FrameNumber",
                        frameNumberText);
                }

                frameNumber = int.Parse(frameNumberMatch.Groups["num"].Value);
            }

            decimal? assignedWeight = null;

            if (assignedWeightIndex >= 0 && assignedWeightIndex < row.Count && !string.IsNullOrWhiteSpace(row[assignedWeightIndex]))
            {
                var assignedWeightText = row[assignedWeightIndex].Trim();

                if (!decimal.TryParse(assignedWeightText, out var parsedWeight) || parsedWeight <= 0)
                {
                    throw new JraValueParseException(
                        JraPageKind.RaceResult,
                        url,
                        "AssignedWeight",
                        assignedWeightText);
                }

                assignedWeight = parsedWeight;
            }

            var trainerName =
                trainerIndex >= 0 && trainerIndex < row.Count && !string.IsNullOrWhiteSpace(row[trainerIndex])
                    ? row[trainerIndex].Trim()
                    : null;

            int? popularity = null;

            if (popularityIndex >= 0 && popularityIndex < row.Count && !string.IsNullOrWhiteSpace(row[popularityIndex]))
            {
                var popularityText = row[popularityIndex].Trim();

                if (!int.TryParse(popularityText, out var parsedPopularity) || parsedPopularity < 1)
                {
                    throw new JraValueParseException(
                        JraPageKind.RaceResult,
                        url,
                        "Popularity",
                        popularityText);
                }

                popularity = parsedPopularity;
            }

            int? bodyWeight = null;
            int? bodyWeightChange = null;

            if (bodyWeightIndex >= 0 && bodyWeightIndex < row.Count && !string.IsNullOrWhiteSpace(row[bodyWeightIndex]))
            {
                var bodyWeightText = row[bodyWeightIndex].Trim();
                var bodyWeightMatch = BodyWeightRegex.Match(bodyWeightText);

                if (!bodyWeightMatch.Success)
                {
                    throw new JraValueParseException(
                        JraPageKind.RaceResult,
                        url,
                        "BodyWeight",
                        bodyWeightText);
                }

                bodyWeight = int.Parse(bodyWeightMatch.Groups["weight"].Value);

                if (bodyWeightMatch.Groups["change"].Success)
                {
                    bodyWeightChange = int.Parse(bodyWeightMatch.Groups["change"].Value);
                }
            }

            string? marginRaw = null;
            var isDeadHeat = false;

            if (marginIndex >= 0 && marginIndex < row.Count)
            {
                if (!string.IsNullOrWhiteSpace(row[marginIndex]))
                {
                    marginRaw = row[marginIndex].Trim();
                    isDeadHeat = marginRaw == "同着";
                }
                else if (status == ResultStatus.Finished && finishPosition is > 1)
                {
                    // 着差欄自体は存在する（列は検出済み）のに、通常完走の2着以下で
                    // 値が完全に空という、Parser異常の可能性が高いケース（依頼書20節）。
                    throw new JraResultConsistencyException(
                        JraPageKind.RaceResult,
                        url,
                        "通常完走の2着以下で着差を取得できませんでした。",
                        "MarginRaw",
                        row[marginIndex]);
                }
            }

            IReadOnlyList<int>? cornerOrders = null;

            if (cornerOrdersIndex >= 0 && cornerOrdersIndex < row.Count && !string.IsNullOrWhiteSpace(row[cornerOrdersIndex]))
            {
                var text = row[cornerOrdersIndex].Trim();
                var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var parsedOrders = new List<int>();

                foreach (var token in tokens)
                {
                    if (!int.TryParse(token, out var order))
                    {
                        throw new JraValueParseException(
                            JraPageKind.RaceResult,
                            url,
                            "CornerOrders",
                            text);
                    }

                    parsedOrders.Add(order);
                }

                cornerOrders = parsedOrders;
            }

            decimal? estimatedLast3F = null;

            if (estimatedLast3FIndex >= 0 && estimatedLast3FIndex < row.Count && !string.IsNullOrWhiteSpace(row[estimatedLast3FIndex]))
            {
                var text = row[estimatedLast3FIndex].Trim();

                if (!decimal.TryParse(text, out var parsed))
                {
                    throw new JraValueParseException(
                        JraPageKind.RaceResult,
                        url,
                        "EstimatedLast3F",
                        text);
                }

                estimatedLast3F = parsed;
            }

            decimal? average1F = null;

            if (average1FIndex >= 0 && average1FIndex < row.Count && !string.IsNullOrWhiteSpace(row[average1FIndex]))
            {
                var text = row[average1FIndex].Trim();

                if (!decimal.TryParse(text, out var parsed))
                {
                    throw new JraValueParseException(
                        JraPageKind.RaceResult,
                        url,
                        "Average1F",
                        text);
                }

                average1F = parsed;
            }

            results.Add(new RaceResultEntry(
                status,
                finishPosition,
                horseNumber,
                horseName,
                jockeyName,
                time,
                OriginalFinishPosition: originalFinishPosition,
                Sex: sex,
                Age: age,
                FrameNumber: frameNumber,
                AssignedWeight: assignedWeight,
                TrainerName: trainerName,
                Popularity: popularity,
                BodyWeight: bodyWeight,
                BodyWeightChange: bodyWeightChange,
                MarginRaw: marginRaw,
                IsDeadHeat: isDeadHeat,
                EstimatedLast3F: estimatedLast3F,
                Average1F: average1F,
                CornerOrders: cornerOrders));
        }

        return results;
    }
}
