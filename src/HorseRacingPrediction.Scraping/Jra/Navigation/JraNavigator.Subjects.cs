using System.Text.RegularExpressions;
using HorseRacingPrediction.Scraping.Browser;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Pages;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Scraping.Jra.Navigation;

public sealed partial class JraNavigator
{
    public async Task<JraSubjectPage> ToSubjectProfileAsync(JraSubjectIdentity subject, CancellationToken cancellationToken = default)
    {
        if (subject.SubjectType == "Horse") return await FindHorseAsync(subject, cancellationToken);
        if (subject.SubjectType != "Trainer") throw new ArgumentException("対象の種別が不正です。");
        await ToKeibaTopAsync(cancellationToken);
        await _browser.ClickAsync("騎手・調教師", cancellationToken);
        var directoryLinks = await _browser.GetLinksAsync(cancellationToken: cancellationToken);
        var profileLink = directoryLinks.FirstOrDefault(l => new Uri(l.Url).AbsolutePath == "/datafile/meikan/trainer.html")
            ?? throw new JraCollectionException("調教師プロフィールの公開リンクが見つかりません。");
        await _browser.ClickLinkAsync(profileLink, cancellationToken);
        foreach (var initial in new[] { "あ行", "か行", "さ行", "た行", "な行", "は行", "ま行", "や行", "ら行", "わ行" })
        {
            var beforeLinks = await _browser.GetLinksAsync(cancellationToken: cancellationToken);
            var group = beforeLinks.FirstOrDefault(l => l.Title.Trim() == initial);
            if (group is not null) await _browser.ClickLinkAsync(group, cancellationToken);
            var links = await _browser.GetLinksAsync(cancellationToken: cancellationToken);
            var matches = links.Where(l => SubjectProfilePageParser.Normalize(Regex.Replace(l.Title, "^(美浦|栗東)\\s*", ""))
                == SubjectProfilePageParser.Normalize(subject.Name)).ToArray();
            if (matches.Length > 1) throw new JraCollectionException("同定不能: 同名の調教師が複数見つかりました。");
            if (matches.Length == 0) continue;
            await _browser.ClickLinkAsync(matches[0], cancellationToken);
            var page = SubjectProfilePageParser.Parse(await _browser.GetDataPageSnapshotAsync(cancellationToken), "Trainer");
            SubjectProfilePageParser.Validate(page, subject);
            return page;
        }
        // 引退者も公開一覧から探す。現役一覧にないことを取得成功として扱わない。
        await ToKeibaTopAsync(cancellationToken);
        await _browser.ClickAsync("騎手・調教師", cancellationToken);
        await _browser.ClickAsync("引退調教師一覧", cancellationToken);
        var retiredLinks = await _browser.GetLinksAsync(cancellationToken: cancellationToken);
        var retiredMatches = retiredLinks.Where(l => SubjectProfilePageParser.Normalize(l.Title) == SubjectProfilePageParser.Normalize(subject.Name)).ToArray();
        if (retiredMatches.Length != 1) throw new JraCollectionException("同定不能: 公開名簿から調教師を一意に確認できませんでした。");
        await _browser.ClickLinkAsync(retiredMatches[0], cancellationToken);
        var result = SubjectProfilePageParser.Parse(await _browser.GetDataPageSnapshotAsync(cancellationToken), "Trainer");
        SubjectProfilePageParser.Validate(result, subject);
        return result;
    }

    private async Task<JraSubjectPage> FindHorseAsync(JraSubjectIdentity subject, CancellationToken token)
    {
        await OpenHorseSearchAsync(subject.Name, token);
        var found = new List<(PageLinkSnapshot Link, int Page)>();
        var pages = new HashSet<string>();
        var pageNumber = 0;
        while (true)
        {
            var snapshot = await _browser.GetDataPageSnapshotAsync(token);
            var view = JraSnapshotView.Create(snapshot);
            var signature = string.Join("|", view.Tables.SelectMany(t => t.Rows).Select(r => string.Join(" ", r)));
            if (!pages.Add(signature)) throw new JraCollectionException("競走馬検索のページ送りが進みません。");
            var links = await _browser.GetLinksAsync(cancellationToken: token);
            var candidates = links.Where(l => SubjectProfilePageParser.Normalize(l.Title) == SubjectProfilePageParser.Normalize(subject.Name))
                .DistinctBy(l => l.Url).ToArray();
            foreach (var link in candidates)
            {
                if (subject.SourceIdentity is not null && link.Url != subject.SourceIdentity) continue;
                if (subject.BirthDate is null) { found.Add((link, pageNumber)); continue; }
                await _browser.ClickLinkAsync(link, token);
                var candidate = SubjectProfilePageParser.Parse(await _browser.GetDataPageSnapshotAsync(token), "Horse");
                if (SubjectProfilePageParser.TryDate(candidate.Profile.Fields.GetValueOrDefault("生年月日") ?? "", out var birth) && birth == subject.BirthDate)
                    found.Add((link, pageNumber));
                await _browser.GoBackAsync(token);
            }
            var next = FindNext(links);
            if (next is null) break;
            await _browser.ClickLinkAsync(next, token); pageNumber++;
        }
        if (found.Count != 1) throw new JraCollectionException(found.Count == 0
            ? "同定不能: 公開検索から対象馬を確認できませんでした。" : "同定不能: 同名の馬が複数います。生年月日を登録して再依頼してください。");
        await OpenHorseSearchAsync(subject.Name, token);
        for (var i = 0; i < found[0].Page; i++)
        {
            var next = FindNext(await _browser.GetLinksAsync(cancellationToken: token)) ?? throw new JraCollectionException("検索ページが変化しました。");
            await _browser.ClickLinkAsync(next, token);
        }
        var currentLinks = await _browser.GetLinksAsync(cancellationToken: token);
        var selected = currentLinks.FirstOrDefault(l => l.Url == found[0].Link.Url && SubjectProfilePageParser.Normalize(l.Title) == SubjectProfilePageParser.Normalize(subject.Name))
            ?? throw new JraCollectionException("対象馬の検索結果が変化しました。");
        await _browser.ClickLinkAsync(selected, token);
        var page = SubjectProfilePageParser.Parse(await _browser.GetDataPageSnapshotAsync(token), "Horse");
        page = page with { Profile = page.Profile with { SourceIdentity = selected.Url } };
        SubjectProfilePageParser.Validate(page, subject);
        return page;
    }

    private async Task OpenHorseSearchAsync(string name, CancellationToken token)
    {
        await ToKeibaTopAsync(token);
        await _browser.ClickAsync("競走馬検索", token);
        await _browser.SetFieldValueAsync("iv_h_name", name, token);
        var search = (await _browser.GetLinksAsync(cancellationToken: token)).FirstOrDefault(l => l.Title.Trim() == "検索" && l.Region == "content")
            ?? throw new JraCollectionException("競走馬の検索操作が見つかりません。");
        await _browser.ClickLinkAsync(search, token);
    }

    private static PageLinkSnapshot? FindNext(IEnumerable<PageLinkSnapshot> links) => links.FirstOrDefault(l =>
        Regex.IsMatch(SubjectProfilePageParser.Normalize(l.Title), @"^(次へ|次のページ|次の[0-9]+件|次)$"));

    public async Task<JraSubjectPage?> NextHorseHistoryPageAsync(JraSubjectPage current, CancellationToken cancellationToken = default)
    {
        if (current.NextPage is null) return null;
        await _browser.ClickLinkAsync(current.NextPage, cancellationToken);
        var page = SubjectProfilePageParser.Parse(await _browser.GetDataPageSnapshotAsync(cancellationToken), "Horse");
        page = page with { Profile = page.Profile with { SourceIdentity = current.Profile.SourceIdentity } };
        SubjectProfilePageParser.Validate(page, new("Horse", current.Profile.Name,
            SubjectProfilePageParser.TryDate(current.Profile.Fields.GetValueOrDefault("生年月日") ?? "", out var birth) ? birth : null,
            current.Profile.SourceIdentity));
        return page;
    }

    public async Task<JraRaceResultPage> ToHorseHistoryResultAsync(JraSubjectIdentity horse, HorseHistoryRaceLink race, CancellationToken cancellationToken = default)
    {
        var page = await ToSubjectProfileAsync(horse, cancellationToken);
        var seen = new HashSet<string>();
        while (page is not null)
        {
            if (!seen.Add(string.Join("|", page.Races.Select(r => r.Key)))) throw new JraCollectionException("出走履歴のページ送りが進みません。");
            var target = page.Races.FirstOrDefault(r => r.Date == race.Date && r.Course == race.Course && r.Link?.Url == race.Link?.Url && r.Link is not null);
            if (target?.Link is not null)
            {
                await _browser.ClickLinkAsync(target.Link, cancellationToken);
                var result = (JraRaceResultPage)new RaceResultPageParser().Parse(await _browser.GetPageSnapshotAsync(cancellationToken: cancellationToken));
                if (result.RaceId.Date != race.Date || result.RaceId.Course != RaceCourseNames.Parse(race.Course)
                    || !result.Results.Any(r => SubjectProfilePageParser.Normalize(r.HorseName ?? "") == SubjectProfilePageParser.Normalize(horse.Name)))
                    throw new JraCollectionException("取得したレースが馬の出走履歴と一致しません。");
                return result;
            }
            page = await NextHorseHistoryPageAsync(page, cancellationToken);
        }
        throw new JraCollectionException("対象レースの公開リンクを再確認できませんでした。");
    }
}
