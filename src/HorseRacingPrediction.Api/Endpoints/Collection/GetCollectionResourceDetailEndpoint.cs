using HorseRacingPrediction.Api.CollectionController;
using EventFlow.EntityFramework;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Endpoints.Collection;

internal static class GetCollectionResourceDetailEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v2/admin/collection/resources/{type}/{provider}/{resourceId}/definitions/{definition}",
            async ([AsParameters] GetCollectionResourceDetailRequest request,
                CollectionPlatformStore store, IServiceProvider services,
                CancellationToken token) =>
            {
                var detail = await store.GetResourceDetailPagedAsync(new(request.Type, request.Provider, request.ResourceId),
                    new(request.Definition), request.RequestHistoryPage ?? request.HistoryPage ?? 1,
                    request.TaskHistoryPage ?? request.HistoryPage ?? 1,
                    request.AttemptHistoryPage ?? request.HistoryPage ?? 1,
                    request.HistoryPageSize ?? 25, token).ConfigureAwait(false);
                if (detail is null) return Results.NotFound();
                var dto = CollectionContractMapper.ToDto(detail);
                var origin = await ResolveOriginAsync(detail,
                    services.GetService<IDbContextProvider<EventStoreDbContext>>(), token).ConfigureAwait(false);
                return Results.Ok(new GetCollectionResourceDetailResponse(dto with { Origin = origin }));
            })
            .Produces<GetCollectionResourceDetailResponse>(StatusCodes.Status200OK);

    private static async Task<CollectionOriginSummaryDto?> ResolveOriginAsync(
        CollectionResourceDetail detail, IDbContextProvider<EventStoreDbContext>? provider,
        CancellationToken token)
    {
        var metadata = detail.LatestTask?.Metadata
            ?? detail.Tasks.FirstOrDefault()?.Metadata;
        if (metadata is null
            || !Enum.TryParse<CollectionResourceType>(metadata.GetValueOrDefault("discoveredFromType"), true, out var type)
            || string.IsNullOrWhiteSpace(metadata.GetValueOrDefault("discoveredFromProvider"))
            || string.IsNullOrWhiteSpace(metadata.GetValueOrDefault("discoveredFromId")))
            return null;

        var origin = new CollectionResourceKeyDto(type,
            metadata["discoveredFromProvider"], metadata["discoveredFromId"]);
        string? name = null;
        try
        {
            if (provider is null)
                return new(origin, null, metadata.GetValueOrDefault("discoveredFromReason"), null, false,
                    "生成元の詳細を取得できません");
            using var db = provider.CreateContext();
            name = type switch
            {
                CollectionResourceType.Race => await db.RaceSummaries.AsNoTracking()
                    .Where(x => x.RaceId == origin.Id).Select(x => x.RaceName)
                    .SingleOrDefaultAsync(token).ConfigureAwait(false),
                CollectionResourceType.Horse => await db.Horses.AsNoTracking()
                    .Where(x => x.HorseId == origin.Id).Select(x => x.RegisteredName)
                    .SingleOrDefaultAsync(token).ConfigureAwait(false),
                CollectionResourceType.Jockey => await db.Jockeys.AsNoTracking()
                    .Where(x => x.JockeyId == origin.Id).Select(x => x.DisplayName)
                    .SingleOrDefaultAsync(token).ConfigureAwait(false),
                CollectionResourceType.Trainer => await db.Trainers.AsNoTracking()
                    .Where(x => x.TrainerId == origin.Id).Select(x => x.DisplayName)
                    .SingleOrDefaultAsync(token).ConfigureAwait(false),
                _ => null,
            };
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // The origin is an optional read-model facet. A deleted or unavailable domain
            // database must not hide the collection detail itself.
        }

        var definition = type switch
        {
            CollectionResourceType.Race => "race-detail",
            CollectionResourceType.Horse => "horse-profile",
            CollectionResourceType.Jockey => "jockey-profile",
            CollectionResourceType.Trainer => "trainer-profile",
            _ => "",
        };
        var detailUrl = string.IsNullOrWhiteSpace(definition) ? null
            : $"/jobs/{type}/{Uri.EscapeDataString(origin.Provider)}/{Uri.EscapeDataString(origin.Id)}/{definition}";
        var available = !string.IsNullOrWhiteSpace(name);
        return new(origin, name, metadata.GetValueOrDefault("discoveredFromReason"), detailUrl,
            available, available ? null : "生成元の詳細を取得できません");
    }
}
