using EventFlow;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Common.Time;
using HorseRacingPrediction.Contracts.Repairs;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace HorseRacingPrediction.Api.Endpoints.Repairs;

internal static class ApplySubjectIdentificationCandidateEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/admin/repairs/subject-identification/candidate/apply",
            async (ApplySubjectIdentificationCandidateRequest request,
                IDbContextProvider<EventStoreDbContext> provider,
                CollectionPlatformStore collectionStore,
                ICommandBus commandBus,
                HttpContext httpContext,
                CancellationToken token) =>
            {
                if (request.NotificationId == Guid.Empty || request.Selection is null
                    || string.IsNullOrWhiteSpace(request.Selection.Name)
                    || string.IsNullOrWhiteSpace(request.Selection.Url))
                    return Results.BadRequest(new[] { "選択する主体識別候補を指定してください。" });

                var selection = request.Selection;
                var selectedName = selection.Name.Trim();
                var normalizedUrl = JraSourceIdentity.NormalizeHorseUrl(selection.Url);
                if (normalizedUrl is null)
                    return Results.BadRequest(new[] { "JRAの競走馬プロフィールURLとして解釈できません。" });

                var persisted = await collectionStore.GetSubjectIdentificationSelectionAsync(
                    request.NotificationId, token).ConfigureAwait(false);
                if (persisted is not null && !MatchesSelection(persisted, selectedName,
                        normalizedUrl.AbsoluteUri, selection.Evidence))
                    return Results.Conflict(new[] { "同じ失敗通知に別の候補が既に適用されています。" });
                if (persisted?.IsFinalized == true)
                    return Accepted(persisted);

                var failures = await collectionStore.GetFailureNotificationsAsync(
                    [request.NotificationId], token).ConfigureAwait(false);
                var failure = failures.SingleOrDefault();
                if (persisted is null && (failure is null || failure.ResolutionStatus != CollectionFailureResolutionStatus.Open
                    || failure.Resource.Type != CollectionResourceType.Horse
                    || !string.Equals(failure.ErrorCode, "SubjectNotIdentified", StringComparison.Ordinal)))
                    return Results.Conflict(new[] { "対象の主体識別失敗状態が変わりました。再読込してください。" });

                CollectionTaskSummary? task = null;
                if (failure is not null)
                {
                    var detail = await collectionStore.GetResourceDetailPagedAsync(
                        failure.Resource, failure.Definition, historyPageSize: 100, cancellationToken: token)
                        .ConfigureAwait(false);
                    task = detail?.Tasks.SingleOrDefault(x => x.TaskId == failure.TaskId)
                        ?? detail?.LatestTask;
                    if (persisted is null)
                    {
                        var attempt = detail?.Attempts.Where(x => x.TaskId == failure.TaskId)
                            .OrderByDescending(x => x.StartedAt).FirstOrDefault();
                        var stored = attempt?.IdentificationCandidates?.SingleOrDefault(x =>
                            string.Equals(x.Name, selectedName, StringComparison.Ordinal)
                            && string.Equals(JraSourceIdentity.NormalizeHorseUrl(x.Url)?.AbsoluteUri,
                                normalizedUrl.AbsoluteUri, StringComparison.Ordinal)
                            && string.Equals(x.Evidence, selection.Evidence, StringComparison.Ordinal));
                        if (stored is null || !string.Equals(attempt?.PageIdentification,
                                "SubjectIdentification:MultipleCandidates", StringComparison.Ordinal))
                            return Results.Conflict(new[] { "選択した候補は保存済み候補と一致しません。候補を再読込してください。" });
                    }
                }

                var canonicalId = persisted?.CanonicalResource.Id
                    ?? DeterministicIdGenerator.BuildHorseId(selectedName, normalizedUrl.AbsoluteUri);
                var canonicalResource = persisted?.CanonicalResource
                    ?? new ResourceKey(CollectionResourceType.Horse, "JRA", canonicalId);
                var canonicalDefinition = persisted?.CanonicalDefinition ?? failure!.Definition;
                var selectedAt = persisted?.SelectedAt ?? JstTime.Now();
                SubjectIdentificationCandidateApplication reservation;
                try
                {
                    reservation = persisted ?? await collectionStore.ReserveSubjectIdentificationCandidateAsync(
                        request.NotificationId, new(selectedName, selection.Url!, selection.Evidence),
                        canonicalResource, canonicalDefinition, normalizedUrl,
                        httpContext.User.Identity?.Name, selectedAt, token).ConfigureAwait(false);
                }
                catch (SubjectIdentificationSelectionConflictException)
                {
                    return Results.Conflict(new[] { "同じ失敗通知に別の候補が既に適用されています。" });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("no longer actionable",
                    StringComparison.Ordinal) || ex.Message.Contains("does not match", StringComparison.Ordinal)
                    || ex.Message.Contains("no selectable", StringComparison.Ordinal))
                {
                    return Results.Conflict(new[] { "対象の失敗状態、収集定義、または候補が変わりました。再読込してください。" });
                }

                canonicalId = reservation.CanonicalResource.Id;
                canonicalResource = reservation.CanonicalResource;
                canonicalDefinition = reservation.CanonicalDefinition;
                // A concurrent same-choice replay may have observed the durable winner
                // after its own request timestamp was computed. All downstream idempotent
                // work must retain the first selector's persisted timestamp.
                selectedAt = reservation.SelectedAt;
                using var db = provider.CreateContext();
                var existing = await db.Horses.AsNoTracking().SingleOrDefaultAsync(
                    x => x.HorseId == canonicalId, token).ConfigureAwait(false);
                if (existing is not null)
                {
                    var expectedName = JraSubjectNameNormalizer.NormalizeIdentityName("Horse", selectedName);
                    if (!string.Equals(JraSubjectNameNormalizer.NormalizeIdentityName("Horse", existing.RegisteredName),
                            expectedName, StringComparison.Ordinal))
                        return Results.Conflict(new[] { "canonical Horse IDの名前が候補と一致しません。" });
                    var profile = await db.Set<JraSubjectProfileReadModel>().AsNoTracking()
                        .SingleOrDefaultAsync(x => x.SubjectId == canonicalId, token).ConfigureAwait(false);
                    if (profile is not null && !JraSourceIdentity.MatchesHorse(profile.SourceIdentity, normalizedUrl.AbsoluteUri))
                        return Results.Conflict(new[] { "canonical Horse IDのJRA識別情報を検証できません。" });
                }
                else
                {
                    var normalizedName = JraSubjectNameNormalizer.CanonicalizeDisplayName("Horse", selectedName);
                    try
                    {
                        var result = await commandBus.PublishAsync(new RegisterHorseCommand(
                            new HorseId(canonicalId), selectedName, normalizedName), token).ConfigureAwait(false);
                        if (!result.IsSuccess)
                            return Results.Conflict(new[] { "canonical Horseを登録できませんでした。" });
                    }
                    catch (InvalidOperationException ex) when (string.Equals(
                        ex.Message, "Horse is already registered.", StringComparison.Ordinal))
                    {
                        var raced = await db.Horses.AsNoTracking().SingleOrDefaultAsync(
                            x => x.HorseId == canonicalId, token).ConfigureAwait(false);
                        if (raced is null || !string.Equals(
                                JraSubjectNameNormalizer.NormalizeIdentityName("Horse", raced.RegisteredName),
                                JraSubjectNameNormalizer.NormalizeIdentityName("Horse", selectedName),
                                StringComparison.Ordinal))
                            return Results.Conflict(new[] { "canonical Horseの登録状態が競合しました。再試行してください。" });
                    }
                }

                var attributes = new Dictionary<string, string>(
                    task?.Metadata ?? new Dictionary<string, string>(), StringComparer.Ordinal)
                {
                    ["name"] = selectedName,
                    ["sourceIdentity"] = normalizedUrl.AbsoluteUri,
                    ["sourceUrl"] = normalizedUrl.AbsoluteUri,
                };
                if (task?.Reason is { } originReason)
                    attributes["discoveredFromReason"] = originReason.ToString();
                var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
                    request.NotificationId, selectedName, normalizedUrl.AbsoluteUri, canonicalId,
                    selection.Evidence ?? string.Empty)))).ToLowerInvariant();
                CollectionRequestReceipt receipt;
                try
                {
                    receipt = await collectionStore.RequestAsync(canonicalResource, canonicalDefinition,
                        await collectionStore.GetCurrentRevisionAsync(canonicalDefinition, token).ConfigureAwait(false),
                        CollectionReason.Recovery, selectedAt, CollectionLane.Normal, (int)CollectionPriority.High,
                        normalizedUrl, $"subject-identification:{request.NotificationId:N}", null,
                        attributes, token, fingerprint).ConfigureAwait(false);
                }
                catch (CollectionRequestIdempotencyMismatchException)
                {
                    return Results.Conflict(new[] { "同じ失敗通知の復旧task内容が競合しています。再試行してください。" });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("active collection task",
                    StringComparison.Ordinal) || ex.Message.Contains("cannot collect", StringComparison.Ordinal))
                {
                    return Results.Conflict(new[] { "canonical Horseの復旧taskを作成できません。再試行してください。" });
                }

                if (receipt.TaskId is not { } canonicalTaskId)
                    return Results.Conflict(new[] { "canonical Horseの復旧taskを作成できません。再試行してください。" });
                SubjectIdentificationCandidateApplication application;
                try
                {
                    application = await collectionStore.FinalizeSubjectIdentificationCandidateAsync(
                        request.NotificationId, canonicalTaskId, receipt.CreatedTask, !receipt.CreatedTask,
                        selectedAt, token).ConfigureAwait(false);
                }
                catch (SubjectIdentificationSelectionConflictException)
                {
                    return Results.Conflict(new[] { "同じ失敗通知の復旧taskが競合しています。再試行してください。" });
                }

                return Results.Accepted($"/api/v2/admin/collection/tasks/{application.CanonicalTaskId:D}",
                    new ApplySubjectIdentificationCandidateResponse(new(
                        application.NotificationId, application.SelectedUrl.AbsoluteUri,
                        application.SelectedName, new(application.CanonicalResource.Type,
                            application.CanonicalResource.Provider, application.CanonicalResource.Id),
                        new(application.CanonicalDefinition.Value), application.CanonicalTaskId,
                        application.Selector, application.SelectedAt, application.CreatedTask,
                        application.ReusedTask)));
            })
            .Produces<ApplySubjectIdentificationCandidateResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);
    }

    private static bool MatchesSelection(SubjectIdentificationCandidateApplication persisted,
        string selectedName, string selectedUrl, string? evidence)
        => string.Equals(persisted.SelectedName, selectedName, StringComparison.Ordinal)
           && string.Equals(persisted.SelectedUrl.AbsoluteUri, selectedUrl, StringComparison.Ordinal)
           && string.Equals(persisted.Evidence, evidence, StringComparison.Ordinal);

    private static IResult Accepted(SubjectIdentificationCandidateApplication persisted)
        => Results.Accepted($"/api/v2/admin/collection/tasks/{persisted.CanonicalTaskId:D}",
            new ApplySubjectIdentificationCandidateResponse(new(
                persisted.NotificationId, persisted.SelectedUrl.AbsoluteUri, persisted.SelectedName,
                new(persisted.CanonicalResource.Type, persisted.CanonicalResource.Provider,
                    persisted.CanonicalResource.Id), new(persisted.CanonicalDefinition.Value),
                persisted.CanonicalTaskId, persisted.Selector, persisted.SelectedAt,
                persisted.CreatedTask, persisted.ReusedTask)));
}
