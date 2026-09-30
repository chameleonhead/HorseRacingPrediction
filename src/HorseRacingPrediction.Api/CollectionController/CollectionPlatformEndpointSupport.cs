using EventFlow.EntityFramework;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common.Time;
using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.CollectionController;

internal static class CollectionPlatformEndpointSupport
{
    internal const string RaceEntryOwnerMigrationBatchId = "migration:race-entry-owners:v2";

    internal static RaceEntryOwnerRepairCandidate ToRaceEntryOwnerRepairCandidate(RacePredictionContextReadModel race)
    {
        var date = race.RaceDate ?? throw new InvalidOperationException("Race date is required.");
        var number = race.RaceNumber ?? throw new InvalidOperationException("Race number is required.");
        var course = HorseRacingPrediction.Contracts.Races.RaceCourseIdentity.ResourceCode(race.RacecourseCode)
            ?? throw new InvalidOperationException($"Unsupported JRA racecourse '{race.RacecourseCode}'.");
        var firstDate = HorseRacingPrediction.Contracts.Common.Time.JstTime.Today()
            .AddDays(-HorseRacingPrediction.Contracts.Collection.JraCollectionPolicy.DefaultRaceCardLookupPeriodDays);
        var eligibility = date >= firstDate ? RaceEntryOwnerRepairEligibility.CardRetrievalCandidate
            : RaceEntryOwnerRepairEligibility.OutsideCardLookupPeriod;
        var reason = eligibility == RaceEntryOwnerRepairEligibility.CardRetrievalCandidate
            ? "出馬表の探索対象期間内です。実際の公開有無は収集時に確認します。"
            : $"出馬表の探索対象期間（{firstDate:yyyy-MM-dd}以降）外のため、現行の公式取得元から補正できません。";
        return new(race.RaceId, $"{date:yyyyMMdd}:{course}:{number}", race.RaceName,
            race.RacecourseCode ?? course, number, race.Entries.Count,
            race.Entries.Count(x => string.IsNullOrWhiteSpace(x.OwnerName)), date, eligibility, reason);
    }

    internal static async Task<IReadOnlyList<RaceEntryOwnerRepairCandidate>> GetRaceEntryOwnerMigrationCandidatesAsync(
        IDbContextProvider<EventStoreDbContext> dbContextProvider, CancellationToken token)
    {
        using var db = dbContextProvider.CreateContext();
        var races = await db.Set<RacePredictionContextReadModel>().AsNoTracking()
            .OrderBy(x => x.RaceDate).ThenBy(x => x.RacecourseCode).ThenBy(x => x.RaceNumber)
            .ToListAsync(token).ConfigureAwait(false);
        return races.Where(x => x.RaceDate.HasValue && x.RaceNumber.HasValue && x.Entries.Count > 0)
            .Select(ToRaceEntryOwnerRepairCandidate).Where(x => x.MissingOwnerCount > 0).ToArray();
    }

    internal static RaceEntryOwnerMigrationProgress BuildRaceEntryOwnerMigrationProgress(
        IReadOnlyList<RaceEntryOwnerRepairCandidate> missing, IReadOnlyList<CollectionBatchResourceStatus> batch)
    {
        var missingIds = missing.Select(x => x.ResourceId).ToHashSet(StringComparer.Ordinal);
        var batchIds = batch.Select(x => x.Resource.Id).ToHashSet(StringComparer.Ordinal);
        var classified = missing.Select(x => batchIds.Contains(x.ResourceId) ? x with
        { Eligibility = RaceEntryOwnerRepairEligibility.ExistingRequest, EligibilityReason = "同じmigration batchの補正要求が既に存在します。" } : x).ToArray();
        var corrected = batch.Count(x => !missingIds.Contains(x.Resource.Id));
        var processing = batch.Count(x => missingIds.Contains(x.Resource.Id) && x.StateStatus != CollectionStateStatus.Unavailable
            && x.LatestTaskStatus is CollectionTaskStatus.Pending or CollectionTaskStatus.Ready or CollectionTaskStatus.Running
                or CollectionTaskStatus.RetryWaiting or CollectionTaskStatus.WaitingDiscovery);
        var failed = batch.Count(x => missingIds.Contains(x.Resource.Id) && x.StateStatus != CollectionStateStatus.Unavailable
            && x.LatestTaskStatus is CollectionTaskStatus.Failed or CollectionTaskStatus.DeadLetter or CollectionTaskStatus.Cancelled);
        var outside = classified.Count(x => x.Eligibility == RaceEntryOwnerRepairEligibility.OutsideCardLookupPeriod);
        var unavailable = outside + batch.Count(x => missingIds.Contains(x.Resource.Id) && x.StateStatus == CollectionStateStatus.Unavailable);
        var eligible = classified.Count(x => x.Eligibility == RaceEntryOwnerRepairEligibility.CardRetrievalCandidate);
        return new(RaceEntryOwnerMigrationBatchId, batch.Count, corrected, processing, failed, unavailable, missing.Count,
            classified, eligible, outside);
    }

    internal static RevisionImpact BuildImpact(RevisionImpactRequest request) => request.ScopeType switch
    {
        RevisionImpactScopeType.All => new(request.ScopeType, string.Empty),
        RevisionImpactScopeType.SpecificResources when request.Resources is { Count: > 0 } =>
            new(request.ScopeType, System.Text.Json.JsonSerializer.Serialize(request.Resources)),
        RevisionImpactScopeType.DateRange when request.From is not null && request.To is not null && request.From <= request.To =>
            new(request.ScopeType, System.Text.Json.JsonSerializer.Serialize(new { From = request.From.Value, To = request.To.Value })),
        RevisionImpactScopeType.NamedCondition when !string.IsNullOrWhiteSpace(request.NamedCondition) =>
            new(request.ScopeType, request.NamedCondition),
        _ => throw new ArgumentException("Revision impact parameters are invalid."),
    };

    internal static async Task<IResult> RecoverFailuresAsync(IReadOnlyList<PendingCollectionFailureNotification> failures,
        int? requestedRevision, CollectionLane lane, int priority, CollectionPlatformStore store, CancellationToken token)
    {
        var taskIds = new List<Guid>(failures.Count);
        var created = 0;
        foreach (var failure in failures)
        {
            var state = await store.GetStateAsync(failure.Resource, failure.Definition, token);
            var revision = requestedRevision ?? state?.RequiredRevision
                ?? throw new InvalidOperationException("Collection state was not found for a pending failure.");
            var receipt = await store.RequestAsync(failure.Resource, failure.Definition, revision, CollectionReason.Recovery,
                HorseRacingPrediction.Contracts.Common.Time.JstTime.Now(), lane, priority, cancellationToken: token);
            if (receipt.CreatedTask) created++;
            if (receipt.TaskId is { } taskId) taskIds.Add(taskId);
        }
        return Results.Accepted(value: new CollectionFailureRecoveryResult(failures.Count, created,
            failures.Count - created, taskIds.Distinct().ToList()));
    }

    internal static async Task<IReadOnlyList<CollectionBulkTarget>> ResolveBulkTargetsAsync(BulkCollectionOperationRequest request,
        CollectionPlatformStore store, IDbContextProvider<EventStoreDbContext> domainProvider,
        IEnumerable<INamedRevisionImpactCondition> conditions, CancellationToken token)
    {
        IReadOnlyList<CollectionBulkTarget> targets;
        if (request.Selection == BulkCollectionSelection.SpecificResources)
            targets = (request.Resources ?? []).Select(x => new CollectionBulkTarget(x)).ToList();
        else if (request.Selection == BulkCollectionSelection.LastCollectedBefore)
            targets = await store.SelectBulkTargetsAsync(new(request.DefinitionId), request.LastCollectedBefore, cancellationToken: token).ConfigureAwait(false);
        else if (request.Selection is BulkCollectionSelection.Failed or BulkCollectionSelection.Stale)
            targets = await store.SelectBulkTargetsAsync(new(request.DefinitionId), status:
                request.Selection == BulkCollectionSelection.Failed ? CollectionStateStatus.Failed : CollectionStateStatus.Stale, cancellationToken: token).ConfigureAwait(false);
        else if (request.Selection == BulkCollectionSelection.RevisionImpact)
            targets = await store.GetRevisionImpactTargetsAsync(new(request.DefinitionId),
                request.ImpactRevision ?? throw new ArgumentException("ImpactRevision is required."), conditions, token).ConfigureAwait(false);
        else
        {
            await using var db = domainProvider.CreateContext();
            var contexts = await db.RacePredictionContexts.AsNoTracking().ToListAsync(token).ConfigureAwait(false);
            IEnumerable<RacePredictionContextReadModel> selected = contexts;
            if (request.Selection == BulkCollectionSelection.HorsesRacedInDateRange)
            {
                if (request.From is null || request.To is null || request.From > request.To) throw new ArgumentException("A valid From/To range is required.");
                selected = contexts.Where(x => x.RaceDate >= request.From && x.RaceDate <= request.To);
            }
            else if (request.Selection == BulkCollectionSelection.HorsesByTrainer)
            {
                if (string.IsNullOrWhiteSpace(request.TrainerId)) throw new ArgumentException("TrainerId is required.");
                selected = contexts.Where(x => x.Entries.Any(e => string.Equals(e.TrainerId, request.TrainerId, StringComparison.Ordinal)));
            }
            else throw new ArgumentOutOfRangeException(nameof(request.Selection));
            targets = selected.SelectMany(x => x.Entries)
                .Where(x => request.Selection != BulkCollectionSelection.HorsesByTrainer || string.Equals(x.TrainerId, request.TrainerId, StringComparison.Ordinal))
                .Select(x => x.HorseId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)
                .Select(x => new CollectionBulkTarget(new(CollectionResourceType.Horse, request.Provider, x))).ToList();
        }
        return await store.ExcludeSuppressedResourcesAsync(targets, token).ConfigureAwait(false);
    }

    internal static string? ValidateRacePeriodRecollection(CreateRacePeriodRecollectionRequest request)
    {
        if (!string.Equals(request.Provider?.Trim(), "JRA", StringComparison.OrdinalIgnoreCase)) return "Provider must be JRA.";
        if (request.From > request.To) return "開始日は終了日以前にしてください。";
        if (request.To > HorseRacingPrediction.Contracts.Common.Time.JstTime.Today()) return "未来日のレースは再取得できません。";
        if (request.To.DayNumber - request.From.DayNumber + 1 > 31) return "期間は31日以内にしてください。";
        return null;
    }
}
