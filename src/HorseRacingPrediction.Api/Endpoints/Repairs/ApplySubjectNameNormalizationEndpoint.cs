using EventFlow;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Jockeys;
using HorseRacingPrediction.Domain.Trainers;
using HorseRacingPrediction.Infrastructure.Persistence;

namespace HorseRacingPrediction.Api.Endpoints.Repairs;

using static HorseRacingPrediction.Api.Endpoints.Repairs.SubjectNameNormalizationService;


internal static class ApplySubjectNameNormalizationEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/admin/repairs/subject-name-normalization/apply", async (
                    ApplySubjectNameNormalizationRequest request,
                    IDbContextProvider<EventStoreDbContext> provider, ICommandBus commandBus,
                    CancellationToken token) =>
                {
                    if (request.Items is null || request.Items.Count == 0)
                        return Results.BadRequest(new[] { "補正対象を選択してください。" });
                    if (request.Items.Count > 50)
                        return Results.BadRequest(new[] { "一度に補正できるのは50件までです。" });
                    if (request.Items.Select(x => (x.SubjectType, x.SubjectId)).Distinct().Count() != request.Items.Count)
                        return Results.BadRequest(new[] { "補正対象が重複しています。" });

                    using var db = provider.CreateContext();
                    var rowsByType = new Dictionary<CollectionResourceType, IReadOnlyList<SubjectNameRow>>();
                    foreach (var type in request.Items.Select(x => x.SubjectType).Distinct())
                    {
                        if (type is not (CollectionResourceType.Horse or CollectionResourceType.Jockey or CollectionResourceType.Trainer))
                            return Results.BadRequest(new[] { "補正対象に対応していない種別が含まれています。" });
                        rowsByType[type] = await GetAllSubjectNamesAsync(db, type, token).ConfigureAwait(false);
                    }

                    var results = new List<SubjectNameNormalizationItemResult>(request.Items.Count);
                    foreach (var item in request.Items)
                    {
                        var rows = rowsByType[item.SubjectType];
                        var row = rows.SingleOrDefault(x => string.Equals(x.Id, item.SubjectId, StringComparison.Ordinal));
                        if (row is null)
                        {
                            results.Add(new(item.SubjectType, item.SubjectId, "Skipped", "対象が見つかりません。"));
                            continue;
                        }
                        var candidate = BuildSubjectNameNormalizationCandidate(item.SubjectType, row, rows);
                        if (!candidate.HasChanges)
                        {
                            results.Add(new(item.SubjectType, item.SubjectId, "Skipped", "すでに正規化されています。"));
                            continue;
                        }
                        if (!string.Equals(candidate.ManifestToken, item.ManifestToken, StringComparison.Ordinal))
                        {
                            results.Add(new(item.SubjectType, item.SubjectId, "Skipped", "検索後に名称が変更されました。再検索してください。"));
                            continue;
                        }
                        if (!candidate.CanApply)
                        {
                            results.Add(new(item.SubjectType, item.SubjectId, "Skipped",
                                candidate.BlockingReason ?? "現在の状態では補正できません。"));
                            continue;
                        }

                        try
                        {
                            var succeeded = item.SubjectType switch
                            {
                                CollectionResourceType.Horse => (await commandBus.PublishAsync(new CorrectHorseDataCommand(
                                    new HorseId(item.SubjectId), candidate.ProposedDisplayName,
                                    candidate.ProposedNormalizedName, reason: SubjectNameNormalizationReason), token)
                                    .ConfigureAwait(false)).IsSuccess,
                                CollectionResourceType.Jockey => (await commandBus.PublishAsync(new CorrectJockeyDataCommand(
                                    new JockeyId(item.SubjectId), candidate.ProposedDisplayName,
                                    candidate.ProposedNormalizedName, reason: SubjectNameNormalizationReason), token)
                                    .ConfigureAwait(false)).IsSuccess,
                                CollectionResourceType.Trainer => (await commandBus.PublishAsync(new CorrectTrainerDataCommand(
                                    new TrainerId(item.SubjectId), candidate.ProposedDisplayName,
                                    candidate.ProposedNormalizedName, reason: SubjectNameNormalizationReason), token)
                                    .ConfigureAwait(false)).IsSuccess,
                                _ => false,
                            };
                            results.Add(new(item.SubjectType, item.SubjectId, succeeded ? "Applied" : "Failed",
                                succeeded ? "名称を補正しました。" : "補正コマンドを完了できませんでした。"));
                        }
                        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                        {
                            results.Add(new(item.SubjectType, item.SubjectId, "Failed", "補正処理を完了できませんでした。"));
                        }
                    }

                    return Results.Ok(new SubjectNameNormalizationApplyResult(request.Items.Count,
                        results.Count(x => x.Status == "Applied"), results.Count(x => x.Status == "Skipped"),
                        results.Count(x => x.Status == "Failed"), results));
                });
    }
}
