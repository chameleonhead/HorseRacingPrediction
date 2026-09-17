using System.Text;
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
        if (subject.SubjectType is not ("Trainer" or "Jockey")) throw new ArgumentException("対象の種別が不正です。");
        var isJockey = subject.SubjectType == "Jockey";
        var label = isJockey ? "騎手" : "調教師";
        await ToKeibaTopAsync(cancellationToken);
        await _browser.ClickForSnapshotAsync("騎手・調教師", cancellationToken);
        var directoryLinks = await _browser.GetLinksAsync(cancellationToken: cancellationToken);
        var profileLink = directoryLinks.FirstOrDefault(l => HasPath(l.Url,
            isJockey ? "/datafile/meikan/jockey.html" : "/datafile/meikan/trainer.html"))
            ?? throw new JraCollectionException($"{label}プロフィールの公開リンクが見つかりません。");
        await _browser.ClickLinkForSnapshotAsync(profileLink, cancellationToken);
        foreach (var initial in new[] { "あ行", "か行", "さ行", "た行", "な行", "は行", "ま行", "や行", "ら行", "わ行" })
        {
            var beforeLinks = await _browser.GetLinksAsync(cancellationToken: cancellationToken);
            var group = beforeLinks.FirstOrDefault(l => l.Title.Trim() == initial);
            if (group is not null) await _browser.ClickLinkForSnapshotAsync(group, cancellationToken);
            var links = await _browser.GetLinksAsync(cancellationToken: cancellationToken);
            var matches = links.Where(l => SubjectProfilePageParser.NormalizeIdentityName(subject.SubjectType,
                    Regex.Replace(l.Title, "^(美浦|栗東)\\s*", ""))
                == SubjectProfilePageParser.NormalizeIdentityName(subject.SubjectType, subject.Name)).ToArray();
            if (matches.Length > 1) throw new JraSubjectIdentificationException(
                JraSubjectIdentificationFailureKind.MultipleCandidates, subject.SubjectType, subject.Name,
                candidates: matches.Select(x => new JraSubjectIdentificationCandidate(x.Title, x.Url)));
            if (matches.Length == 0) continue;
            await _browser.ClickLinkForSnapshotAsync(matches[0], cancellationToken);
            await _browser.WaitForContentAsync([$"{label}情報", subject.Name], cancellationToken);
            var page = SubjectProfilePageParser.Parse(await _browser.GetDataPageSnapshotAsync(cancellationToken), subject.SubjectType);
            SubjectProfilePageParser.Validate(page, subject);
            return page;
        }
        // 引退者も公開一覧から探す。現役一覧にないことを取得成功として扱わない。
        await ToKeibaTopAsync(cancellationToken);
        await _browser.ClickForSnapshotAsync("騎手・調教師", cancellationToken);
        await _browser.ClickForSnapshotAsync(isJockey ? "引退騎手一覧" : "引退調教師一覧", cancellationToken);
        var retiredLinks = await _browser.GetLinksAsync(cancellationToken: cancellationToken);
        var retiredMatches = retiredLinks.Where(l => SubjectProfilePageParser.NormalizeIdentityName(subject.SubjectType, l.Title)
            == SubjectProfilePageParser.NormalizeIdentityName(subject.SubjectType, subject.Name)).ToArray();
        if (retiredMatches.Length != 1) throw new JraSubjectIdentificationException(
            retiredMatches.Length == 0 ? JraSubjectIdentificationFailureKind.NoCandidate
                : JraSubjectIdentificationFailureKind.MultipleCandidates,
            subject.SubjectType, subject.Name,
            candidates: retiredMatches.Select(x => new JraSubjectIdentificationCandidate(x.Title, x.Url)));
        await _browser.ClickLinkForSnapshotAsync(retiredMatches[0], cancellationToken);
        await _browser.WaitForContentAsync([$"{label}情報", subject.Name], cancellationToken);
        var result = SubjectProfilePageParser.Parse(await _browser.GetDataPageSnapshotAsync(cancellationToken), subject.SubjectType);
        SubjectProfilePageParser.Validate(result, subject);
        return result;
    }

    private async Task<JraSubjectPage> FindHorseAsync(JraSubjectIdentity subject, CancellationToken token)
    {
        await OpenHorseSearchAsync(subject.Name, token);
        var found = new List<(PageLinkSnapshot Link, int Page, JraSubjectPage? Parsed)>();
        var evidence = new BoundedHorseCandidateEvidence();
        var pages = new HashSet<string>();
        var pageNumber = 0;
        while (true)
        {
            var snapshot = await _browser.GetDataPageSnapshotAsync(token);
            var view = JraSnapshotView.Create(snapshot);
            var signature = string.Join("|", view.Tables.SelectMany(t => t.Rows).Select(r => string.Join(" ", r)));
            if (!pages.Add(signature)) throw new JraCollectionException("競走馬検索のページ送りが進みません。");
            var links = view.Links.Select(l => new PageLinkSnapshot(l.Url, l.Title)).ToArray();
            var candidates = links.Where(l => SubjectProfilePageParser.Normalize(l.Title) == SubjectProfilePageParser.Normalize(subject.Name))
                .DistinctBy(l => l.Url).ToArray();
            foreach (var candidateLink in candidates)
            {
                if (!evidence.TryAdd(candidateLink))
                {
                    throw new JraSubjectIdentificationException(
                        JraSubjectIdentificationFailureKind.MultipleCandidates,
                        subject.SubjectType,
                        subject.Name,
                        candidates: evidence.Items.Select(item =>
                            new JraSubjectIdentificationCandidate(item.Title, item.Url)).ToArray(),
                        requestedUrl: subject.SourceIdentity,
                        finalUrl: _browser.CurrentUrl);
                }
            }
            foreach (var link in candidates)
            {
                if (subject.SourceIdentity is not null && link.Url != subject.SourceIdentity) continue;
                if (subject.BirthDate is null) { found.Add((link, pageNumber, null)); continue; }
                await _browser.ClickLinkForSnapshotAsync(link, token);
                await _browser.WaitForContentAsync(["競走馬情報", subject.Name], token);
                var candidate = SubjectProfilePageParser.Parse(await _browser.GetDataPageSnapshotAsync(token), "Horse");
                if (SubjectProfilePageParser.TryDate(candidate.Profile.Fields.GetValueOrDefault("生年月日") ?? "", out var birth) && birth == subject.BirthDate)
                    found.Add((link, pageNumber, candidate));
                await _browser.GoBackForSnapshotAsync(token);
            }
            var next = FindNext(links);
            if (next is null) break;
            await _browser.ClickLinkForSnapshotAsync(next, token); pageNumber++;
        }
        var recordedCandidates = evidence.Items.Select(x =>
            new JraSubjectIdentificationCandidate(x.Title, x.Url)).ToArray();
        if (found.Count != 1)
            throw new JraSubjectIdentificationException(
                found.Count == 0
                    ? JraSubjectIdentificationFailureKind.NoCandidate
                    : JraSubjectIdentificationFailureKind.MultipleCandidates,
                subject.SubjectType, subject.Name, candidates: recordedCandidates,
                requestedUrl: subject.SourceIdentity, finalUrl: _browser.CurrentUrl);
        for (var i = pageNumber; i > found[0].Page; i--)
        {
            await _browser.GoBackForSnapshotAsync(token);
        }
        var currentSnapshot = JraSnapshotView.Create(await _browser.GetDataPageSnapshotAsync(token));
        var currentLinks = currentSnapshot.Links.Select(l => new PageLinkSnapshot(l.Url, l.Title)).ToArray();
        var selected = currentLinks.FirstOrDefault(l => l.Url == found[0].Link.Url && SubjectProfilePageParser.Normalize(l.Title) == SubjectProfilePageParser.Normalize(subject.Name))
            ?? throw new JraCollectionException("対象馬の検索結果が変化しました。");
        await _browser.ClickLinkForSnapshotAsync(selected, token);
        await _browser.WaitForContentAsync(["競走馬情報", subject.Name], token);
        var page = found[0].Parsed ?? SubjectProfilePageParser.Parse(await _browser.GetDataPageSnapshotAsync(token), "Horse");
        page = page with { Profile = page.Profile with { SourceIdentity = selected.Url } };
        try
        {
            SubjectProfilePageParser.Validate(page, subject);
        }
        catch (JraSubjectIdentificationException ex)
        {
            throw ex.WithNavigation(recordedCandidates, selected.Url, _browser.CurrentUrl);
        }
        return page;
    }

    internal sealed class BoundedHorseCandidateEvidence
    {
        internal const int MaximumCandidates = 32;
        internal const int MaximumBytes = 256 * 1024;
        private readonly List<PageLinkSnapshot> _items = [];
        private readonly HashSet<string> _urls = new(StringComparer.OrdinalIgnoreCase);
        private int _bytes;

        internal IReadOnlyList<PageLinkSnapshot> Items => _items;

        internal bool TryAdd(PageLinkSnapshot item)
        {
            if (!_urls.Add(item.Url)) return true;
            var addedBytes = Encoding.UTF8.GetByteCount(item.Title) + Encoding.UTF8.GetByteCount(item.Url);
            if (_items.Count == MaximumCandidates || _bytes + addedBytes > MaximumBytes) return false;
            _items.Add(item);
            _bytes += addedBytes;
            return true;
        }
    }

    private async Task OpenHorseSearchAsync(string name, CancellationToken token)
    {
        await ToKeibaTopAsync(token);
        await _browser.ClickForSnapshotAsync("競走馬検索", token);
        await _browser.SetFieldValueForSnapshotAsync("iv_h_name", name, token);
        await _browser.SubmitFormForSnapshotAsync("iv_h_name", token);
    }

    internal static bool HasPath(string url, string expectedPath)
    {
        if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            return false;

        // Unixでは先頭が / のJRA相対リンクを file: の絶対URIとして解釈する。
        // ブラウザー上の絶対URLとして扱うのはHTTP(S)だけに限定する。
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return absolute.AbsolutePath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase);
        }

        var path = url.Split(['?', '#'], 2)[0];
        return path.Equals(expectedPath, StringComparison.OrdinalIgnoreCase) ||
               expectedPath.EndsWith('/' + path, StringComparison.OrdinalIgnoreCase);
    }

    private static PageLinkSnapshot? FindNext(IEnumerable<PageLinkSnapshot> links) => links.FirstOrDefault(l =>
        Regex.IsMatch(SubjectProfilePageParser.Normalize(l.Title), @"^(次へ|次のページ|次の[0-9]+件|次)$"));

    public async Task<JraSubjectPage?> NextHorseHistoryPageAsync(JraSubjectPage current, CancellationToken cancellationToken = default)
    {
        if (current.NextPage is null) return null;
        await _browser.ClickLinkForSnapshotAsync(current.NextPage, cancellationToken);
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
                await _browser.ClickLinkForSnapshotAsync(target.Link, cancellationToken);
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
