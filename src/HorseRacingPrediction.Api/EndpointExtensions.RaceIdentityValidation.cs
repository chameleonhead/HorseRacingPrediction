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
            if (item.HorseNumber <= 0 || !seenNumbers.Add(item.HorseNumber))
            {
                Reject("InvalidHorseNumber", "HorseNumber must be positive and unique.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(item.HorseName))
            {
                Reject("MissingHorseName", "Horse identity is required before applying collected data.");
                continue;
            }
            if (!string.IsNullOrWhiteSpace(item.HorseSourceIdentity)
                && !JraSourceIdentity.TryNormalizeHorse(item.HorseSourceIdentity, out _))
            {
                Reject("InvalidHorseSourceIdentity", "The supplied Horse source identity is invalid.");
                continue;
            }
            var name = Shared.JraSubjectNameNormalizer.CanonicalizeDisplayName("Horse", item.HorseName);
            var horseId = DeterministicIdGenerator.BuildHorseId(name, item.HorseSourceIdentity);
            var old = existing?.Entries.FirstOrDefault(entry => entry.HorseNumber == item.HorseNumber);
            if (!seenHorses.Add(horseId)
                || (old is not null && old.HorseId != horseId)
                || (existing?.Entries.Any(entry => entry.HorseId == horseId
                    && entry.HorseNumber != item.HorseNumber) ?? false))
                Reject("RaceEntryIdentityMismatch", "Collected Horse identity does not match the saved horse-number assignment; no data was written.");
        }
        return failures.Count == 0 ? null : Results.Ok(new Shared.DeclareRaceResultBulkResponse(
            raceId, failures.Select(item => $"{item.ErrorCode}: {item.Key} — {item.Message}").ToArray(),
            failures, CorePersisted: false));
    }
}
