using HorseRacingPrediction.Application.Queries.ReadModels;
using ApiContracts = HorseRacingPrediction.Contracts;

using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static partial class RaceResultBulkService
{
    internal static IResult CollectedIdentityRejection(string raceId, string code, string? detail = null) =>
        Results.Ok(new HorseRacingPrediction.Contracts.Races.DeclareRaceResultBulkResponse(
            new HorseRacingPrediction.Contracts.Races.DeclareRaceResultBulkResultDto(raceId, [detail ?? code],
                [new("Entry", "HorseIdentity", "Rejected", code,
                    detail ?? "Horse identity could not be resolved safely; no data was written.")],
                CorePersisted: false)));

    // Validate the entire envelope before creating any related subject. Collection cannot repair identity.
    internal static IResult? ValidateCollectedEntryIdentities(HorseRacingPrediction.Contracts.Races.DeclareRaceResultBulkInputDto request,
        RacePredictionContextReadModel? existing, string raceId,
        IReadOnlyDictionary<HorseRacingPrediction.Contracts.Races.RaceResultEntryBulkDto, string>? resolved = null)
    {
        var failures = new List<HorseRacingPrediction.Contracts.Races.RaceResultBulkItemOutcomeDto>();
        var seenNumbers = new HashSet<int>();
        var seenHorses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in request.Entries ?? [])
        {
            var key = $"HorseNumber={item.HorseNumber}";
            void Reject(string code, string message) => failures.Add(new("Entry", key, "Rejected", code, message));
            if (item.ParticipationStatus is { } status && !Enum.IsDefined(status))
            {
                Reject("InvalidParticipationStatus", "ParticipationStatus must be a known value.");
                continue;
            }
            if (item.HorseNumber is <= 0 || item.GateNumber is < 1 or > 8 || (!request.IsRaceCard && item.HorseNumber is null)
                || (item.HorseNumber is { } number && !seenNumbers.Add(number)))
            {
                Reject("InvalidHorseNumber", "HorseNumber must be positive and unique, and GateNumber must be within 1–8 when supplied.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(item.HorseName))
            {
                Reject("MissingHorseName", "Horse identity is required before applying collected data.");
                continue;
            }
            if (((item.HorseNumber is null || item.ParticipationStatus is HorseRacingPrediction.Contracts.Races.RaceEntryParticipationStatus.Cancelled or HorseRacingPrediction.Contracts.Races.RaceEntryParticipationStatus.Excluded)
                    && string.IsNullOrWhiteSpace(item.HorseSourceIdentity))
                || !string.IsNullOrWhiteSpace(item.HorseSourceIdentity)
                && !JraSourceIdentity.TryNormalizeHorse(item.HorseSourceIdentity, out _))
            {
                Reject("InvalidHorseSourceIdentity", "The supplied Horse source identity is invalid.");
                continue;
            }
            var name = JraSubjectNameNormalizer.CanonicalizeDisplayName("Horse", item.HorseName);
            var horseId = resolved?.GetValueOrDefault(item) ?? DeterministicIdGenerator.BuildHorseId(name, item.HorseSourceIdentity);
            if (!seenHorses.Add(horseId))
                Reject("RaceEntryIdentityMismatch", "Collected Horse identity must be unique within the race; no data was written.");
        }
        var incoming = (request.Entries ?? []).Where(item => !string.IsNullOrWhiteSpace(item.HorseName))
            .Select(item => new
            {
                HorseId = resolved?.GetValueOrDefault(item) ?? DeterministicIdGenerator.BuildHorseId(
                HorseRacingPrediction.Contracts.Common.JraSubjectNameNormalizer.CanonicalizeDisplayName("Horse", item.HorseName!), item.HorseSourceIdentity),
                item.HorseNumber
            }).ToArray();
        var effectiveAssignments = (existing?.Entries ?? [])
            .Where(entry => !incoming.Any(item => item.HorseId == entry.HorseId))
            .Select(entry => new { entry.HorseId, entry.HorseNumber, Origin = "Existing" })
            .Concat(incoming.Select(item => new
            {
                item.HorseId,
                HorseNumber = item.HorseNumber
                    ?? existing?.Entries.FirstOrDefault(entry => entry.HorseId == item.HorseId)?.HorseNumber,
                Origin = "Incoming"
            }))
            .Where(item => item.HorseNumber.HasValue)
            .ToArray();
        foreach (var collision in effectiveAssignments.GroupBy(item => item.HorseNumber!.Value)
                     .Where(group => group.Count() > 1))
        {
            var assignments = string.Join(", ", collision.OrderBy(item => item.Origin, StringComparer.Ordinal)
                .ThenBy(item => item.HorseId, StringComparer.Ordinal)
                .Select(item => $"{item.Origin}HorseId={item.HorseId}"));
            failures.Add(new("Entry", $"HorseNumber={collision.Key}", "Rejected", "InvalidHorseNumber",
                $"Effective horse number is assigned to multiple horse identities ({assignments}); no data was written."));
        }
        return failures.Count == 0 ? null : Results.Ok(new HorseRacingPrediction.Contracts.Races.DeclareRaceResultBulkResponse(
            new HorseRacingPrediction.Contracts.Races.DeclareRaceResultBulkResultDto(
                raceId, failures.Select(item => $"{item.ErrorCode}: {item.Key} — {item.Message}").ToArray(),
                failures, CorePersisted: false)));
    }

    internal static async Task<Dictionary<HorseRacingPrediction.Contracts.Races.RaceResultEntryBulkDto, string>> ResolveCollectedHorseIdentitiesAsync(
        HorseRacingPrediction.Contracts.Races.DeclareRaceResultBulkInputDto request, HorseRacingPrediction.Infrastructure.Persistence.EventStoreDbContext db,
        HorseRacingPrediction.CollectionOperations.CollectionPlatform.CollectionPlatformStore collection, CancellationToken token)
    {
        var resolved = new Dictionary<HorseRacingPrediction.Contracts.Races.RaceResultEntryBulkDto, string>();
        var horses = await CollectionIdentityResolver.LoadHorsesAsync(db, collection, token);
        foreach (var item in request.Entries ?? [])
            if (!string.IsNullOrWhiteSpace(item.HorseName)
                && (string.IsNullOrWhiteSpace(item.HorseSourceIdentity) || JraSourceIdentity.TryNormalizeHorse(item.HorseSourceIdentity, out _)))
            {
                try
                {
                    resolved[item] = CollectionIdentityResolver.ResolveHorse(
                        horses, item.HorseName, item.HorseSourceIdentity, null);
                }
                catch (InvalidOperationException ex)
                {
                    JraSourceIdentity.TryNormalizeHorse(item.HorseSourceIdentity, out var normalizedSource);
                    throw new CollectedHorseIdentityException(ex.Message,
                        $"{ex.Message}: Horse={item.HorseName}; Source={normalizedSource}");
                }
            }
        return resolved;
    }

    internal sealed class CollectedHorseIdentityException(string code, string detail) : InvalidOperationException(detail)
    {
        internal string Code { get; } = code;
        internal string Detail { get; } = detail;
    }
}
