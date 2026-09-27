using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Contracts.Time;

namespace HorseRacingPrediction.Api.Endpoints.Repairs;

using static HorseRacingPrediction.Api.Endpoints.Repairs.SubjectIdentificationRepairService;


internal static class DismissSubjectIdentificationFailuresEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/admin/repairs/subject-identification/dismiss",
                    async (DismissSubjectIdentificationFailuresRequest request, CollectionPlatformStore collectionStore,
                        CancellationToken token) =>
                    {
                        if (request.NotificationIds is null || request.NotificationIds.Count == 0)
                            return Results.BadRequest(new[] { "対応不要にする対象を指定してください。" });
                        if (request.NotificationIds.Distinct().Count() != request.NotificationIds.Count)
                            return Results.BadRequest(new[] { "NotificationIdが重複しています。" });

                        var selected = await collectionStore.GetFailureNotificationsAsync(request.NotificationIds, token)
                            .ConfigureAwait(false);
                        if (selected.Count != request.NotificationIds.Count || selected.Any(x => !IsSubjectIdentificationFailure(x)))
                            return Results.BadRequest(new[] { "主体識別情報の補正候補ではない対象が含まれています。" });

                        CollectionFailureDismissalResult result;
                        try
                        {
                            result = await collectionStore.DismissFailureNotificationsAsync(
                                request.NotificationIds, JstTime.Now(), token).ConfigureAwait(false);
                        }
                        catch (KeyNotFoundException)
                        {
                            return Results.Conflict(new[] { "対象の失敗状態が変わりました。再読込してください。" });
                        }
                        if (result.HasRecoveryConflict)
                            return Results.Conflict(new[] { "再収集が開始された対象が含まれています。再読込してください。" });

                        return Results.Ok(new DismissSubjectIdentificationFailuresResponse(
                            result.SelectedCount, result.DismissedCount, result.AlreadyClosedCount));
                    });
    }
}
