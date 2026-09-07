using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Scraping.Jra.Parsing;

public static class SubjectProfilePageParser
{
    public static string Normalize(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC), @"\s+", "");
    public static JraSubjectPage Parse(PageSnapshot snapshot, string subjectType)
    {
        var prefix = subjectType == "Horse" ? "競走馬情報" : "調教師情報";
        var heading = snapshot.Headings.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
        if (heading is null) throw new JraCollectionException(prefix + "の見出しを確認できません。");
        var name = Regex.Split(heading[prefix.Length..].Trim(), subjectType == "Horse" ? "[A-Za-z（(]" : "[（(]")[0].Trim();
        if (name.Length == 0) throw new JraCollectionException("プロフィールの名前を取得できません。");
        var fields = new Dictionary<string,string>();
        foreach (var row in snapshot.Tables.Where(t => t.Headers.SequenceEqual(new[] { "項目", "値" })).SelectMany(t=>t.Rows))
            if(row.Count >= 2 && !string.IsNullOrWhiteSpace(row[0]) && !string.IsNullOrWhiteSpace(row[1]))
                fields[row[0].Trim()] = row[1].Trim();
        if (!fields.TryGetValue("生年月日", out var birthText) || !TryDate(birthText, out var birth))
            throw new JraCollectionException("同定に必要な生年月日を取得できません。");
        // 馬は実在リンクの公開URLを識別子として保持。POSTのみの調教師ページは氏名・生年月日の複合キー。
        var sourceIdentity = subjectType == "Horse" ? snapshot.Url : $"Trainer:{Normalize(name)}:{birth:yyyy-MM-dd}";
        var races = new List<HorseHistoryRaceLink>();
        foreach (var table in snapshot.Tables.Where(t => t.Headers.Contains("年月日") && t.Headers.Contains("レース名")))
        {
            int Index(string key) => table.Headers.ToList().IndexOf(key);
            for (var i=0;i<table.Rows.Count;i++)
            {
                var row=table.Rows[i];
                string Cell(string key) => Index(key) is var col && col>=0 && col<row.Count ? row[col].Trim() : "";
                if (Cell("年月日") == "年月日") continue;
                var date = TryDate(Cell("年月日"), out var day) ? day : (DateOnly?)null;
                var course = Cell("場"); var raceName = Cell("レース名");
                var fragment = table.GetCell(i,Index("レース名"))?.Fragments.FirstOrDefault(f=>f.TagName.Equals("a",StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(f.Href));
                var link = fragment is null ? null : new PageLinkSnapshot(fragment.Href!, fragment.Text);
                var reason = RaceCourseNames.Parse(course) == RaceCourse.Unknown || link is null
                    ? "JRA結果ページがない履歴（地方・海外等）" : null;
                if (date is null && reason is null) throw new JraCollectionException("出走履歴の日付を取得できません: "+raceName);
                races.Add(new(date,course,raceName,link,reason));
            }
        }
        var next = snapshot.Links.FirstOrDefault(l => Regex.IsMatch(Normalize(l.Title), @"^(次へ|次のページ|次の[0-9]+件|次)$"));
        return new(new(subjectType,name,sourceIdentity,snapshot.Url,fields,DateTimeOffset.UtcNow),races.DistinctBy(x=>x.Key).ToArray(),next);
    }
    public static void Validate(JraSubjectPage page, JraSubjectIdentity expected)
    {
        if (page.Profile.SubjectType != expected.SubjectType || Normalize(page.Profile.Name) != Normalize(expected.Name))
            throw new JraCollectionException("同定不能: 取得したプロフィールの名前が対象と一致しません。");
        if (expected.BirthDate is { } birth && (!TryDate(page.Profile.Fields.GetValueOrDefault("生年月日") ?? "", out var found) || found != birth))
            throw new JraCollectionException("同定不能: 生年月日が対象と一致しません。");
        if (expected.SourceIdentity is not null && expected.SourceIdentity != page.Profile.SourceIdentity)
            throw new JraCollectionException("同定不能: 公開識別子が保存済み情報と一致しません。");
    }
    public static bool TryDate(string value, out DateOnly date) => DateOnly.TryParseExact(value.Trim(),
        new[] {"yyyy年M月d日","yyyy/MM/dd","yyyy/M/d"}, CultureInfo.InvariantCulture,DateTimeStyles.None,out date);
}
