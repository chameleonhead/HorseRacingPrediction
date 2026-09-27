using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace HorseRacingPrediction.Api.Endpoints.Repairs;

internal static class SubjectNameNormalizationService
{
    internal const string SubjectNameNormalizationReason = "その他設定: 登録済み名称の正規化";


    internal static async Task<SubjectNameSearchPage> SearchSubjectNamesAsync(EventStoreDbContext db,
        CollectionResourceType type, string query, int page, int pageSize, CancellationToken token)
    {
        var skip = (page - 1) * pageSize;
        if (type == CollectionResourceType.Horse)
        {
            var source = db.Horses.AsNoTracking().Where(x => x.HorseId.Contains(query)
                || x.RegisteredName.Contains(query) || x.NormalizedName.Contains(query));
            return new(await source.OrderBy(x => x.RegisteredName).ThenBy(x => x.HorseId).Skip(skip).Take(pageSize)
                .Select(x => new SubjectNameRow(x.HorseId, x.RegisteredName, x.NormalizedName))
                .ToListAsync(token).ConfigureAwait(false), await source.CountAsync(token).ConfigureAwait(false));
        }
        if (type == CollectionResourceType.Jockey)
        {
            var source = db.Jockeys.AsNoTracking().Where(x => x.JockeyId.Contains(query)
                || x.DisplayName.Contains(query) || x.NormalizedName.Contains(query));
            return new(await source.OrderBy(x => x.DisplayName).ThenBy(x => x.JockeyId).Skip(skip).Take(pageSize)
                .Select(x => new SubjectNameRow(x.JockeyId, x.DisplayName, x.NormalizedName))
                .ToListAsync(token).ConfigureAwait(false), await source.CountAsync(token).ConfigureAwait(false));
        }
        if (type == CollectionResourceType.Trainer)
        {
            var source = db.Trainers.AsNoTracking().Where(x => x.TrainerId.Contains(query)
                || x.DisplayName.Contains(query) || x.NormalizedName.Contains(query));
            return new(await source.OrderBy(x => x.DisplayName).ThenBy(x => x.TrainerId).Skip(skip).Take(pageSize)
                .Select(x => new SubjectNameRow(x.TrainerId, x.DisplayName, x.NormalizedName))
                .ToListAsync(token).ConfigureAwait(false), await source.CountAsync(token).ConfigureAwait(false));
        }
        return new([], 0);
    }

    internal static async Task<IReadOnlyList<SubjectNameRow>> GetAllSubjectNamesAsync(EventStoreDbContext db,
        CollectionResourceType type, CancellationToken token) => type switch
        {
            CollectionResourceType.Horse => await db.Horses.AsNoTracking()
                .Select(x => new SubjectNameRow(x.HorseId, x.RegisteredName, x.NormalizedName))
                .ToListAsync(token).ConfigureAwait(false),
            CollectionResourceType.Jockey => await db.Jockeys.AsNoTracking()
                .Select(x => new SubjectNameRow(x.JockeyId, x.DisplayName, x.NormalizedName))
                .ToListAsync(token).ConfigureAwait(false),
            CollectionResourceType.Trainer => await db.Trainers.AsNoTracking()
                .Select(x => new SubjectNameRow(x.TrainerId, x.DisplayName, x.NormalizedName))
                .ToListAsync(token).ConfigureAwait(false),
            _ => [],
        };

    internal static SubjectNameNormalizationCandidate BuildSubjectNameNormalizationCandidate(CollectionResourceType type,
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

    internal static string CreateSubjectNameManifest(CollectionResourceType type, SubjectNameRow row,
        string proposedDisplay, string proposedNormalized) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('|', type, row.Id, row.DisplayName, row.NormalizedName, proposedDisplay, proposedNormalized))))
        .ToLowerInvariant();

    internal sealed record SubjectNameRow(string Id, string DisplayName, string NormalizedName);
    internal sealed record SubjectNameSearchPage(IReadOnlyList<SubjectNameRow> Rows, int TotalCount);
}
