using EventFlow.EntityFramework;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Api;

public sealed class DomainRaceResourceIdentityResolver(IDbContextProvider<EventStoreDbContext> provider)
    : IRaceResourceIdentityResolver
{
    public string? Resolve(string resourceId, IReadOnlyDictionary<string, string> attributes)
    {
        var explicitId = attributes.GetValueOrDefault("domainRaceId");
        using var db = provider.CreateContext();
        var parts = resourceId.Split(':');
        if (parts.Length == 3 && DateOnly.TryParseExact(parts[0], "yyyyMMdd", out var date)
            && RaceCourseIdentity.Canonicalize(parts[1]) is { } course
            && int.TryParse(parts[2], out var number) && number is >= 1 and <= 12)
        {
            var candidates = db.RacePredictionContexts.AsNoTracking()
                .Where(x => x.RaceDate == date && x.RaceNumber == number).ToList();
            string resolved;
            try { resolved = CollectionIdentityResolver.ResolveRace(candidates, date, course, number); }
            catch (InvalidOperationException) { return null; }
            return string.IsNullOrWhiteSpace(explicitId) || explicitId == resolved ? resolved : null;
        }
        // A direct domain key is already the binding; contradictory metadata must not override it.
        if (resourceId.StartsWith("race-", StringComparison.Ordinal) && Guid.TryParseExact(resourceId[5..], "D", out _))
            return string.IsNullOrWhiteSpace(explicitId) || explicitId == resourceId ? resourceId : null;
        return null;
    }
}
