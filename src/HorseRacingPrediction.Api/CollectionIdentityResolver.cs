using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api;

internal static class CollectionIdentityResolver
{
    internal static async Task<string> RaceAsync(EventStoreDbContext db, DateOnly date, string course, int number, CancellationToken token)
    {
        var canonical = RaceCourseIdentity.Canonicalize(course) ?? course.Trim();
        var races = await db.RacePredictionContexts.AsNoTracking()
            .Where(x => x.RaceDate == date && x.RaceNumber == number).ToListAsync(token);
        return ResolveRace(races, date, canonical, number);
    }

    internal static string ResolveRace(IEnumerable<HorseRacingPrediction.Application.Queries.ReadModels.RacePredictionContextReadModel> races,
        DateOnly date, string course, int number)
    {
        var canonical = RaceCourseIdentity.Canonicalize(course) ?? course.Trim();
        var matches = races.Where(x => x.RaceDate == date && x.RaceNumber == number
            && (RaceCourseIdentity.Canonicalize(x.RacecourseCode) ?? x.RacecourseCode?.Trim()) == canonical).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("AmbiguousRaceIdentity");
        return matches.SingleOrDefault()?.RaceId ?? DeterministicIdGenerator.BuildRaceId(date, canonical, number);
    }

    internal sealed record HorseIdentityRow(string HorseId, string RegisteredName, DateOnly? BirthDate, string? SourceIdentity,
        bool HasNameDerivedIdentity = false);

    internal static async Task<List<HorseIdentityRow>> LoadHorsesAsync(EventStoreDbContext db,
        CollectionPlatformStore? collection, CancellationToken token)
    {
        var rows = await (from horse in db.Horses.AsNoTracking()
                          where !db.HorseIdentityRepairRedirects.Any(redirect => redirect.SourceHorseId == horse.HorseId)
                          join profile in db.Set<HorseRacingPrediction.Application.Queries.ReadModels.JraSubjectProfileReadModel>().AsNoTracking()
                              on horse.HorseId equals profile.SubjectId into profiles
                          from profile in profiles.DefaultIfEmpty()
                          select new HorseIdentityRow(horse.HorseId, horse.RegisteredName, horse.BirthDate,
                              profile == null ? null : profile.SourceIdentity)).ToListAsync(token);
        static bool IsNameDerived(string id, string name) => id == DeterministicIdGenerator.BuildHorseId(name)
            || id == DeterministicIdGenerator.BuildEntityId("horse", DeterministicIdGenerator.NormalizeKey(name));
        var unresolved = rows.Where(x => string.IsNullOrWhiteSpace(x.SourceIdentity) && !IsNameDerived(x.HorseId, x.RegisteredName))
            .Select(x => x.HorseId).ToArray();
        // A later display-name correction must not erase the proof behind a legacy fallback ID.
        // Horse aggregate sequence 1 is HorseRegistered; read it without rewriting historical events.
        var registrations = unresolved.Length == 0 ? [] : await db.Set<EventFlow.EntityFramework.EventStores.EventEntity>()
            .AsNoTracking().Where(x => unresolved.Contains(x.AggregateId) && x.AggregateSequenceNumber == 1)
            .Select(x => new { x.AggregateId, x.Data }).ToListAsync(token);
        var originalNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            using var data = System.Text.Json.JsonDocument.Parse(registration.Data);
            var name = data.RootElement.EnumerateObject().FirstOrDefault(x => x.Name.Equals("RegisteredName", StringComparison.OrdinalIgnoreCase)).Value;
            if (name.ValueKind == System.Text.Json.JsonValueKind.String && name.GetString() is { } originalName)
                originalNames[registration.AggregateId] = originalName;
        }
        rows = rows.Select(x => x with
        {
            HasNameDerivedIdentity = IsNameDerived(x.HorseId, x.RegisteredName)
            || originalNames.TryGetValue(x.HorseId, out var originalName) && IsNameDerived(x.HorseId, originalName)
            && JraSubjectNameNormalizer.NormalizeIdentityName("Horse", originalName)
                == JraSubjectNameNormalizer.NormalizeIdentityName("Horse", x.RegisteredName)
        }).ToList();
        if (collection is null) return rows;

        var metadataCandidates = rows.Where(x => string.IsNullOrWhiteSpace(x.SourceIdentity)
            && !x.HasNameDerivedIdentity).Select(x => x.HorseId).ToArray();
        var metadataSources = await collection.GetHorseSourceIdentitiesAsync(metadataCandidates, token);
        return rows.Select(x => metadataSources.TryGetValue(x.HorseId, out var source)
                && JraSourceIdentity.TryNormalizeHorse(source, out _)
                && x.HorseId != DeterministicIdGenerator.BuildHorseId(x.RegisteredName, source)
            ? x with { SourceIdentity = source }
            : x).ToList();
    }

    internal static async Task<string> HorseAsync(EventStoreDbContext db, string name, string? source, DateOnly? birth, CancellationToken token) =>
        ResolveHorse(await LoadHorsesAsync(db, null, token), name, source, birth);

    internal static async Task<string> HorseAsync(EventStoreDbContext db, CollectionPlatformStore collection,
        string name, string? source, DateOnly? birth, CancellationToken token) =>
        ResolveHorse(await LoadHorsesAsync(db, collection, token), name, source, birth);

    internal static string ResolveHorse(IReadOnlyList<HorseIdentityRow> horses, string name, string? source, DateOnly? birth)
    {
        static bool IsOpaqueLegacyId(string horseId) => horseId.StartsWith("horse-", StringComparison.Ordinal)
            && Guid.TryParse(horseId["horse-".Length..], out var value)
            && value.ToString("D")[14] == '4';
        static bool IsLegacyRouteDerivedId(string horseId, string? sourceIdentity)
        {
            var normalizedUrl = JraSourceIdentity.NormalizeHorseUrl(sourceIdentity);
            const string prefix = "?CNAME=";
            if (normalizedUrl is null || !normalizedUrl.Query.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            var cname = Uri.UnescapeDataString(normalizedUrl.Query[prefix.Length..]);
            return horseId == DeterministicIdGenerator.BuildEntityId("horse", $"JRA|{cname}");
        }

        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("MissingHorseName");
        if (!string.IsNullOrWhiteSpace(source) && !JraSourceIdentity.TryNormalizeHorse(source, out _))
            throw new InvalidOperationException("InvalidHorseSourceIdentity");
        var canonical = JraSubjectNameNormalizer.NormalizeIdentityName("Horse", name);
        var id = DeterministicIdGenerator.BuildHorseId(name, source);
        var hasSource = !string.IsNullOrWhiteSpace(source);
        var named = horses.Where(x => JraSubjectNameNormalizer.NormalizeIdentityName("Horse", x.RegisteredName) == canonical).ToArray();
        if (!hasSource && named.Any(x => !string.IsNullOrWhiteSpace(x.SourceIdentity) || !x.HasNameDerivedIdentity))
            throw new InvalidOperationException("HorseIdentityEvidenceRequired");
        var sourceMatches = hasSource
            ? horses.Where(x => x.HorseId == id || JraSourceIdentity.MatchesHorse(x.SourceIdentity, source)).ToArray()
            : [];
        var matches = hasSource
            ? sourceMatches.Length > 0
                ? sourceMatches
                : horses.Where(x => IsLegacyRouteDerivedId(x.HorseId, source)).ToArray()
            : horses.Where(x => x.HorseId == id || named.Any(n => n.HorseId == x.HorseId)).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("AmbiguousHorseIdentity");
        var found = matches.SingleOrDefault();
        if (found is not null)
        {
            if (JraSubjectNameNormalizer.NormalizeIdentityName("Horse", found.RegisteredName) != canonical
                || birth is not null && found.BirthDate is not null && found.BirthDate != birth
                || hasSource && !string.IsNullOrWhiteSpace(found.SourceIdentity)
                    && !JraSourceIdentity.MatchesHorse(found.SourceIdentity, source))
                throw new InvalidOperationException("HorseIdentityConflict");
            return found.HorseId;
        }
        if (hasSource)
        {
            // Pre-source Horse aggregates were created both from names and from caller-supplied opaque IDs.
            // A single source-less namesake is the only safe adoption candidate when its first official
            // JRA identity arrives; multiple candidates or a source-bound namesake remain fail-closed.
            var legacyCandidates = named.Where(x => (x.HasNameDerivedIdentity || IsOpaqueLegacyId(x.HorseId))
                && string.IsNullOrWhiteSpace(x.SourceIdentity)
                && x.HorseId != id).ToArray();
            if (legacyCandidates.Length > 1)
                throw new InvalidOperationException("AmbiguousHorseIdentity");
            if (legacyCandidates.Length == 1)
            {
                if (named.Any(x => x.HorseId != legacyCandidates[0].HorseId)
                    || birth is not null && legacyCandidates[0].BirthDate is not null
                        && legacyCandidates[0].BirthDate != birth)
                    throw new InvalidOperationException("HorseIdentityConflict");
                return legacyCandidates[0].HorseId;
            }
            if (named.Any(x => string.IsNullOrWhiteSpace(x.SourceIdentity)))
                throw new InvalidOperationException("HorseIdentityConflict");
        }
        return id;
    }
}
