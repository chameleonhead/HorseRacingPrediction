using System.Security.Cryptography;
using System.Text;
using EventFlow;
using EventFlow.Commands;
using EventFlow.EntityFramework;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Jockeys;
using HorseRacingPrediction.Domain.Trainers;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Api;

public static partial class EndpointExtensions
{
    private const string SubjectNameNormalizationReason = "その他設定: 登録済み名称の正規化";

    private static void MapSubjectNameNormalizationEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/admin/repairs/subject-name-normalization", async (
            ResourceType subjectType, string? query, int? page, int? pageSize,
            IDbContextProvider<EventStoreDbContext> provider, CancellationToken token) =>
        {
            if (subjectType is not (ResourceType.Horse or ResourceType.Jockey or ResourceType.Trainer))
                return Results.BadRequest(new[] { "対象種別は競走馬、騎手、調教師から選択してください。" });
            var search = query?.Trim();
            if (string.IsNullOrWhiteSpace(search))
                return Results.BadRequest(new[] { "名称またはIDを入力してください。" });
            var actualPage = Math.Max(1, page ?? 1);
            var actualPageSize = Math.Clamp(pageSize ?? 25, 1, 50);
            using var db = provider.CreateContext();
            var searchPage = await SearchSubjectNamesAsync(db, subjectType, search, actualPage, actualPageSize, token)
                .ConfigureAwait(false);
            var all = await GetAllSubjectNamesAsync(db, subjectType, token).ConfigureAwait(false);
            var items = searchPage.Rows
                .Select(row => BuildSubjectNameNormalizationCandidate(subjectType, row, all)).ToArray();
            return Results.Ok(new SubjectNameNormalizationPage(items, searchPage.TotalCount, actualPage, actualPageSize));
        });

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
            var rowsByType = new Dictionary<ResourceType, IReadOnlyList<SubjectNameRow>>();
            foreach (var type in request.Items.Select(x => x.SubjectType).Distinct())
            {
                if (type is not (ResourceType.Horse or ResourceType.Jockey or ResourceType.Trainer))
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
                        ResourceType.Horse => (await commandBus.PublishAsync(new CorrectHorseDataCommand(
                            new HorseId(item.SubjectId), candidate.ProposedDisplayName,
                            candidate.ProposedNormalizedName, reason: SubjectNameNormalizationReason), token)
                            .ConfigureAwait(false)).IsSuccess,
                        ResourceType.Jockey => (await commandBus.PublishAsync(new CorrectJockeyDataCommand(
                            new JockeyId(item.SubjectId), candidate.ProposedDisplayName,
                            candidate.ProposedNormalizedName, reason: SubjectNameNormalizationReason), token)
                            .ConfigureAwait(false)).IsSuccess,
                        ResourceType.Trainer => (await commandBus.PublishAsync(new CorrectTrainerDataCommand(
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

    private static async Task<SubjectNameSearchPage> SearchSubjectNamesAsync(EventStoreDbContext db,
        ResourceType type, string query, int page, int pageSize, CancellationToken token)
    {
        var skip = (page - 1) * pageSize;
        if (type == ResourceType.Horse)
        {
            var source = db.Horses.AsNoTracking().Where(x => x.HorseId.Contains(query)
                || x.RegisteredName.Contains(query) || x.NormalizedName.Contains(query));
            return new(await source.OrderBy(x => x.RegisteredName).ThenBy(x => x.HorseId).Skip(skip).Take(pageSize)
                .Select(x => new SubjectNameRow(x.HorseId, x.RegisteredName, x.NormalizedName))
                .ToListAsync(token).ConfigureAwait(false), await source.CountAsync(token).ConfigureAwait(false));
        }
        if (type == ResourceType.Jockey)
        {
            var source = db.Jockeys.AsNoTracking().Where(x => x.JockeyId.Contains(query)
                || x.DisplayName.Contains(query) || x.NormalizedName.Contains(query));
            return new(await source.OrderBy(x => x.DisplayName).ThenBy(x => x.JockeyId).Skip(skip).Take(pageSize)
                .Select(x => new SubjectNameRow(x.JockeyId, x.DisplayName, x.NormalizedName))
                .ToListAsync(token).ConfigureAwait(false), await source.CountAsync(token).ConfigureAwait(false));
        }
        if (type == ResourceType.Trainer)
        {
            var source = db.Trainers.AsNoTracking().Where(x => x.TrainerId.Contains(query)
                || x.DisplayName.Contains(query) || x.NormalizedName.Contains(query));
            return new(await source.OrderBy(x => x.DisplayName).ThenBy(x => x.TrainerId).Skip(skip).Take(pageSize)
                .Select(x => new SubjectNameRow(x.TrainerId, x.DisplayName, x.NormalizedName))
                .ToListAsync(token).ConfigureAwait(false), await source.CountAsync(token).ConfigureAwait(false));
        }
        return new([], 0);
    }

    private static async Task<IReadOnlyList<SubjectNameRow>> GetAllSubjectNamesAsync(EventStoreDbContext db,
        ResourceType type, CancellationToken token) => type switch
        {
            ResourceType.Horse => await db.Horses.AsNoTracking()
                .Select(x => new SubjectNameRow(x.HorseId, x.RegisteredName, x.NormalizedName))
                .ToListAsync(token).ConfigureAwait(false),
            ResourceType.Jockey => await db.Jockeys.AsNoTracking()
                .Select(x => new SubjectNameRow(x.JockeyId, x.DisplayName, x.NormalizedName))
                .ToListAsync(token).ConfigureAwait(false),
            ResourceType.Trainer => await db.Trainers.AsNoTracking()
                .Select(x => new SubjectNameRow(x.TrainerId, x.DisplayName, x.NormalizedName))
                .ToListAsync(token).ConfigureAwait(false),
            _ => [],
        };

    private static SubjectNameNormalizationCandidate BuildSubjectNameNormalizationCandidate(ResourceType type,
        SubjectNameRow row, IReadOnlyList<SubjectNameRow> all)
    {
        var subjectType = type.ToString();
        var proposedDisplay = JraSubjectNameNormalizer.CanonicalizeDisplayName(subjectType, row.DisplayName);
        var proposedNormalized = JraSubjectNameNormalizer.NormalizeIdentityName(subjectType, row.DisplayName);
        var conflicts = string.IsNullOrWhiteSpace(proposedNormalized) ? [] : all
            .Where(x => !string.Equals(x.Id, row.Id, StringComparison.Ordinal)
                && string.Equals(JraSubjectNameNormalizer.NormalizeIdentityName(subjectType, x.DisplayName),
                    proposedNormalized, StringComparison.Ordinal))
            .Select(x => x.Id).Order(StringComparer.Ordinal).ToArray();
        var hasChanges = !string.Equals(row.DisplayName, proposedDisplay, StringComparison.Ordinal)
            || !string.Equals(row.NormalizedName, proposedNormalized, StringComparison.Ordinal);
        var canApply = hasChanges && proposedDisplay.Length > 0 && proposedNormalized.Length > 0
            && conflicts.Length == 0;
        var evaluation = !hasChanges ? "NoChanges"
            : proposedDisplay.Length == 0 || proposedNormalized.Length == 0 ? "EmptyResult"
            : conflicts.Length > 0 ? "Conflict" : "Ready";
        var blockingReason = evaluation switch
        {
            "NoChanges" => "すでに共通規則で正規化されています。",
            "EmptyResult" => "正規化後の名称が空になるため補正できません。",
            "Conflict" => $"同じ正規化名を持つ別IDがあります: {string.Join(", ", conflicts)}",
            _ => null,
        };
        return new(type, row.Id, row.DisplayName, row.NormalizedName, proposedDisplay, proposedNormalized,
            hasChanges, canApply, evaluation, blockingReason, conflicts,
            CreateSubjectNameManifest(type, row, proposedDisplay, proposedNormalized));
    }

    private static string CreateSubjectNameManifest(ResourceType type, SubjectNameRow row,
        string proposedDisplay, string proposedNormalized) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('|', type, row.Id, row.DisplayName, row.NormalizedName, proposedDisplay, proposedNormalized))))
        .ToLowerInvariant();

    private sealed record SubjectNameRow(string Id, string DisplayName, string NormalizedName);
    private sealed record SubjectNameSearchPage(IReadOnlyList<SubjectNameRow> Rows, int TotalCount);
}
