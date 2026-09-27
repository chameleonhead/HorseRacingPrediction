using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Api.Endpoints.Owners;

internal static class OwnerEndpointService
{
    internal static async Task<List<OwnerSummaryResponse>> BuildOwnersAsync(EventStoreDbContext dbContext, CancellationToken cancellationToken)
    {
        var horses = await dbContext.Horses.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var contexts = await dbContext.RacePredictionContexts.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var currentNames = horses.Where(x => !string.IsNullOrWhiteSpace(x.OwnerName)).Select(x => x.OwnerName!).ToList();
        var participations = contexts.SelectMany(x => x.Entries.Select(e => (x.RaceDate, e.OwnerName)))
            .Where(x => !string.IsNullOrWhiteSpace(x.OwnerName)).ToList();
        var mappingRows = await dbContext.OwnerAliasMappings.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var mappings = mappingRows.ToDictionary(x => x.NormalizedAlias, x => x.OwnerId, StringComparer.Ordinal);
        var displayNames = mappingRows.Where(x => x.IsDisplayName).GroupBy(x => x.OwnerId).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.CreatedAt).First().AliasName, StringComparer.Ordinal);
        string OwnerGroupKey(string name)
            => OwnerIdentityContract.ResolveId(name, mappings)!;
        return currentNames.Concat(participations.Select(x => x.OwnerName!))
            .GroupBy(OwnerGroupKey, StringComparer.Ordinal)
            .Where(x => x.Key.Length > 0)
            .Select(group =>
            {
                var variants = group.Concat(mappingRows.Where(x => x.OwnerId == group.Key).Select(x => x.AliasName))
                    .Distinct(StringComparer.Ordinal).OrderBy(x => x).ToList();
                var displayName = displayNames.GetValueOrDefault(group.Key) ?? variants.OrderByDescending(name => currentNames.Count(x => x == name)).ThenByDescending(name => group.Count(x => x == name)).First();
                var groupParticipations = participations.Where(x => OwnerGroupKey(x.OwnerName!) == group.Key).ToList();
                return new OwnerSummaryResponse(
                    group.Key, displayName, variants,
                    horses.Count(x => x.OwnerName is not null && OwnerGroupKey(x.OwnerName) == group.Key),
                    groupParticipations.Count,
                    groupParticipations.Select(x => x.RaceDate).DefaultIfEmpty().Max());
            }).ToList();
    }

    internal static string NormalizeOwnerName(string value) => OwnerIdentityContract.NormalizeName(value);

    internal static string CreateOwnerId(string normalizedName)
        => OwnerIdentityContract.CreateId(normalizedName);

    internal static string? ResolveOwnerName(IReadOnlyDictionary<string, string> ownerNamesByHorseId, string? horseId)
        => !string.IsNullOrWhiteSpace(horseId) && ownerNamesByHorseId.TryGetValue(horseId, out var ownerName)
            ? ownerName
            : null;

    internal static string? ResolveOwnerId(string? ownerName, IReadOnlyDictionary<string, string> mappings)
        => OwnerIdentityContract.ResolveId(ownerName, mappings);
}
