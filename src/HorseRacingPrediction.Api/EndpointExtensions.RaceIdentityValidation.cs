using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Queries.ReadModels;
using Shared = HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api;

public static partial class EndpointExtensions
{
    // Validate the entire envelope before creating any related subject. Collection cannot repair identity.
    private static IResult? ValidateCollectedEntryIdentities(Shared.DeclareRaceResultBulkRequest request,
        RacePredictionContextReadModel? existing, string raceId)
    {
        var failures = new List<Shared.DeclareRaceResultBulkItemOutcome>();
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
            if (((item.HorseNumber is null || item.ParticipationStatus is Shared.RaceEntryParticipationStatus.Cancelled or Shared.RaceEntryParticipationStatus.Excluded)
                    && string.IsNullOrWhiteSpace(item.HorseSourceIdentity))
                || !string.IsNullOrWhiteSpace(item.HorseSourceIdentity)
                && !JraSourceIdentity.TryNormalizeHorse(item.HorseSourceIdentity, out _))
            {
                Reject("InvalidHorseSourceIdentity", "The supplied Horse source identity is invalid.");
                continue;
            }
            var name = Shared.JraSubjectNameNormalizer.CanonicalizeDisplayName("Horse", item.HorseName);
            var horseId = DeterministicIdGenerator.BuildHorseId(name, item.HorseSourceIdentity);
            if (!seenHorses.Add(horseId))
                Reject("RaceEntryIdentityMismatch", "Collected Horse identity must be unique within the race; no data was written.");
        }
        var incoming = (request.Entries ?? []).Where(item => !string.IsNullOrWhiteSpace(item.HorseName))
            .Select(item => new
            {
                HorseId = DeterministicIdGenerator.BuildHorseId(
                Shared.JraSubjectNameNormalizer.CanonicalizeDisplayName("Horse", item.HorseName!), item.HorseSourceIdentity),
                item.HorseNumber
            }).ToArray();
        var effectiveNumbers = (existing?.Entries ?? []).Where(entry => !incoming.Any(item => item.HorseId == entry.HorseId))
            .Select(entry => entry.HorseNumber).Concat(incoming.Select(item => item.HorseNumber
                ?? existing?.Entries.FirstOrDefault(entry => entry.HorseId == item.HorseId)?.HorseNumber))
            .Where(number => number.HasValue).ToArray();
        if (effectiveNumbers.Distinct().Count() != effectiveNumbers.Length)
            failures.Add(new("Entry", "HorseNumber", "Rejected", "InvalidHorseNumber", "Effective horse numbers must be unique; no data was written."));
        return failures.Count == 0 ? null : Results.Ok(new Shared.DeclareRaceResultBulkResponse(
            raceId, failures.Select(item => $"{item.ErrorCode}: {item.Key} — {item.Message}").ToArray(),
            failures, CorePersisted: false));
    }
}
