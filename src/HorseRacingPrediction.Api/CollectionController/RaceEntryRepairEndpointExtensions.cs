using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using EventFlow;
using EventFlow.EventStores;
using EventFlow.Subscribers;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed record RaceEntryRepairHorse(string SourceUrl, int HorseNumber, int GateNumber, string OwnerName);
public sealed record RaceEntryRepairManifest(int ExpectedVersion, string SourceUrl, DateTimeOffset ObservedAt,
    string? GradeCode, IReadOnlyList<RaceEntryRepairHorse> Horses, string SourceSnapshotSha256,
    string? HoldOperationId = null, long HoldGeneration = 0);
public sealed record ApplyRaceEntryRepairRequest(string OperationId, string Fingerprint, RaceEntryRepairManifest Manifest);
public sealed record HoldRaceEntryRepairRequest(string OperationId, long ExpectedGeneration, string Reason);
public sealed record ReleaseRaceEntryRepairRequest(string OperationId, string HoldOperationId, long HoldGeneration,
    int ExpectedVersion, string AssignmentFingerprint, string? RepairOperationId = null,
    string? RepairFingerprint = null, bool CancelRepair = false);

public static class RaceEntryRepairEndpointExtensions
{
    public static IEndpointRouteBuilder MapRaceEntryRepairEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/races/{raceId}/entry-repair/hold", async (string raceId,
            CollectionPlatformStore collection, CancellationToken token) => Results.Ok(await collection.GetRaceRepairHoldAsync(raceId, token)));
        endpoints.MapGet("/api/admin/races/{raceId}/entry-repair/assignment-fence", async (string raceId,
            RaceEntryRepairInspector inspector, RaceWriteCoordinator coordinator, CollectionPlatformStore collection, CancellationToken token) =>
        {
            await using var held = await LockAsync(raceId, inspector, coordinator, token);
            var hold = await collection.GetRaceRepairHoldAsync(raceId, token);
            if (hold is { IsActive: true } || await coordinator.ReadBarrierAsync(raceId, token) is { Verified: false })
                return Results.Conflict(new { code = "RaceRepairHeld" });
            return Results.Ok(new
            {
                generation = hold?.Generation ?? 0,
                assignmentFingerprint = await coordinator.AssignmentFingerprintAsync(raceId, token)
            });
        });
        endpoints.MapPost("/api/admin/races/{raceId}/entry-repair/hold", async (string raceId,
            HoldRaceEntryRepairRequest request, RaceEntryRepairInspector inspector, RaceWriteCoordinator coordinator,
            CollectionPlatformStore collection, CancellationToken token) =>
        {
            await using var held = await LockAsync(raceId, inspector, coordinator, token);
            try
            {
                var state = await collection.HoldRaceForRepairAsync(raceId, request.OperationId, request.ExpectedGeneration,
                    request.Reason, DateTimeOffset.UtcNow, token, await coordinator.AssignmentFingerprintAsync(raceId, token));
                // A race with no collection state still needs a durable, deferred revision-4 intent.
                if (state.IsActive && !state.Definitions.Contains("race-detail"))
                {
                    var race = (await inspector.InspectAsync(raceId, token)).Race;
                    string[] japanese = ["札幌", "函館", "福島", "新潟", "東京", "中山", "中京", "京都", "阪神", "小倉"];
                    string[] english = ["Sapporo", "Hakodate", "Fukushima", "Niigata", "Tokyo", "Nakayama", "Chukyo", "Kyoto", "Hanshin", "Kokura"];
                    var course = Array.IndexOf(japanese, race.RacecourseCode);
                    if (course < 0 || race.RaceDate is null || race.RaceNumber is null)
                        return Results.Conflict(new { code = "RepairHoldNeedsCollectionIdentity", hold = state });
                    await collection.RequestAsync(new(ResourceType.Race, "JRA", $"{race.RaceDate:yyyyMMdd}:{english[course]}:{race.RaceNumber}"),
                        new("race-detail"), 4, CollectionReason.Recovery, DateTimeOffset.UtcNow,
                        batchId: "repair-hold:" + request.OperationId, attributes: new Dictionary<string, string> { ["domainRaceId"] = raceId },
                        cancellationToken: token);
                }
                return Results.Ok(await collection.GetRaceRepairHoldAsync(raceId, token));
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { code = "InvalidRepairHold", message = ex.Message }); }
            catch (InvalidOperationException) { return Results.Conflict(new { code = "RepairHoldConflict" }); }
        });
        endpoints.MapPost("/api/admin/races/{raceId}/entry-repair/release", async (string raceId,
            ReleaseRaceEntryRepairRequest request, RaceEntryRepairInspector inspector, RaceWriteCoordinator coordinator,
            CollectionPlatformStore collection, CancellationToken token) =>
        {
            await using var held = await LockAsync(raceId, inspector, coordinator, token);
            var hold = await collection.GetRaceRepairHoldAsync(raceId, token);
            if (hold is null || hold.OperationId != request.HoldOperationId || hold.Generation != request.HoldGeneration)
                return Results.Conflict(new { code = "StaleRepairHold" });
            if (hold.IsActive)
            {
                var inspection = await inspector.InspectAsync(raceId, token);
                var barrier = await coordinator.ReadBarrierAsync(raceId, token);
                if (!hold.IsQuiescent || inspection.Blockers.Count != 0 || inspection.Version != request.ExpectedVersion
                    || await coordinator.AssignmentFingerprintAsync(raceId, token) != request.AssignmentFingerprint)
                    return Results.Conflict(new { code = "RepairReleaseNotVerified" });
                if (request.CancelRepair ? inspection.PreviousRepair is not null || barrier is not null
                    : barrier is not { Verified: true } || barrier.OperationId != request.RepairOperationId
                        || barrier.Fingerprint != request.RepairFingerprint || inspection.PreviousRepair?.OperationId != request.RepairOperationId)
                    return Results.Conflict(new { code = "RepairReleaseNotVerified" });
            }
            try
            {
                return Results.Ok(await collection.ReleaseRaceRepairHoldAsync(raceId, request.HoldOperationId, request.HoldGeneration,
                    request.OperationId, request.AssignmentFingerprint, DateTimeOffset.UtcNow, token));
            }
            catch (ArgumentException) { return Results.BadRequest(new { code = "InvalidRepairRelease" }); }
            catch (InvalidOperationException) { return Results.Conflict(new { code = "RepairReleaseConflict" }); }
        });
        endpoints.MapGet("/api/admin/races/{raceId}/entry-repair", async (string raceId,
            RaceEntryRepairInspector inspector, RaceWriteCoordinator coordinator, CancellationToken token) =>
        {
            await using var held = await LockAsync(raceId, inspector, coordinator, token);
            return Results.Ok(await inspector.InspectAsync(raceId, token));
        });
        endpoints.MapPost("/api/admin/races/{raceId}/entry-repair/preview", async (string raceId,
            RaceEntryRepairManifest manifest, RaceEntryRepairInspector inspector, RaceWriteCoordinator coordinator,
            CollectionPlatformStore collection, CancellationToken token) =>
        {
            await using var held = await LockAsync(raceId, inspector, coordinator, token);
            var inspection = await inspector.InspectAsync(raceId, token);
            try
            {
                var plan = BuildPlan(inspection.Race, manifest, inspection.Version);
                var blockers = inspection.Blockers.ToList();
                if (inspection.PreviousRepair is not null) blockers.Add("AlreadyRepaired");
                if (await coordinator.ReadBarrierAsync(raceId, token) is not null) blockers.Add("RepairBarrierExists");
                var hold = await collection.GetRaceRepairHoldAsync(raceId, token);
                if (!MatchesHold(hold, manifest)) blockers.Add("RepairHoldRequired");
                else if (!hold!.IsQuiescent) blockers.Add("RepairHoldNotQuiescent");
                return Results.Ok(new
                {
                    eligible = blockers.Count == 0,
                    plan.Fingerprint,
                    inspection.Version,
                    previousEntries = inspection.Race.Entries,
                    entries = plan.Entries,
                    manifest.GradeCode,
                    inspection.ReferenceCounts,
                    blockers,
                    hold,
                    sourceEvidence = manifest,
                    sourceAssurance = "OperatorProvidedSnapshotRequiresIndependentReview"
                });
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { code = "InvalidRepairManifest", message = ex.Message }); }
        });
        endpoints.MapPost("/api/admin/races/{raceId}/entry-repair/apply", async (string raceId,
            ApplyRaceEntryRepairRequest request, RaceEntryRepairInspector inspector, RaceWriteCoordinator coordinator,
            CollectionPlatformStore collection, ICommandBus commands, IEventStore events,
            IDomainEventPublisher publisher, CancellationToken token) =>
        {
            if (!Guid.TryParse(request.OperationId, out _)) return Results.BadRequest(new { code = "InvalidOperationId" });
            await using var held = await LockAsync(raceId, inspector, coordinator, token);
            var inspection = await inspector.InspectAsync(raceId, token);
            var previous = inspection.PreviousRepair;
            var basis = previous is null ? inspection.Race : inspection.Race with { Entries = previous.PreviousEntries };
            RepairPlan plan;
            try { plan = BuildPlan(basis, request.Manifest, previous is null ? inspection.Version : request.Manifest.ExpectedVersion); }
            catch (ArgumentException ex) { return Results.BadRequest(new { code = "InvalidRepairManifest", message = ex.Message }); }
            if (plan.Fingerprint != request.Fingerprint) return Results.Conflict(new { code = "StaleRepairPreview" });
            var barrier = await coordinator.ReadBarrierAsync(raceId, token);
            if (barrier is not null && (barrier.OperationId != request.OperationId || barrier.Fingerprint != request.Fingerprint))
                return Results.Conflict(new { code = "DifferentRepairPending" });
            if (previous is not null && (previous.OperationId != request.OperationId || previous.Fingerprint != request.Fingerprint))
                return Results.Conflict(new { code = "AlreadyRepaired" });
            var hold = await collection.GetRaceRepairHoldAsync(raceId, token);
            if (!MatchesHold(hold, request.Manifest)) return Results.Conflict(new { code = "RepairHoldRequired" });
            if (!hold!.IsQuiescent) return Results.Conflict(new { code = "RepairHoldNotQuiescent" });
            // A retry may finish this event's projections, but may never disregard independent references.
            var blockers = inspection.Blockers.Where(x => previous is null || !x.StartsWith("ProjectionMismatch:", StringComparison.Ordinal)).ToArray();
            if (blockers.Length != 0) return Results.Conflict(new { code = "RepairBlocked", blockers });
            string backupHash;
            try
            {
                backupHash = await coordinator.BackupRepairAsync(raceId, request.OperationId, JsonSerializer.Serialize(request),
                    path => collection.BackupForRaceRepairAsync(path, token), token, requireExisting: previous is not null);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            { return Results.Conflict(new { code = "RepairBackupNotVerified" }); }
            coordinator.WriteBarrier(raceId, new(request.OperationId, request.Fingerprint, false));
            if (previous is null)
            {
                var execution = await commands.PublishAsync(new RepairRaceEntryAssignmentsCommand(new RaceId(raceId), inspection.Version,
                    request.OperationId, request.Fingerprint, request.Manifest.SourceUrl, request.Manifest.ObservedAt,
                    plan.Entries, request.Manifest.GradeCode, JsonSerializer.Serialize(request.Manifest)), token);
                if (!execution.IsSuccess) return Results.Conflict(new { code = "RepairCommandFailed" });
            }
            else
            {
                var stream = await events.LoadEventsAsync<RaceAggregate, RaceId>(new RaceId(raceId), token);
                await publisher.PublishAsync(stream.Where(x => x.GetAggregateEvent() is RaceEntryAssignmentsRepaired).ToArray(), token);
            }
            var verified = await inspector.InspectAsync(raceId, token);
            if (verified.PreviousRepair?.OperationId != request.OperationId || verified.PreviousRepair.Fingerprint != request.Fingerprint
                || verified.Version != request.Manifest.ExpectedVersion + 1
                || JsonSerializer.Serialize(verified.Race.Entries.OrderBy(x => x.HorseNumber)) != JsonSerializer.Serialize(plan.Entries))
                return Results.Conflict(new { code = "RepairEventNotVerified" });
            if (verified.Blockers.Count != 0) return Results.Conflict(new { code = "RepairProjectionPending", blockers = verified.Blockers });
            coordinator.WriteBarrier(raceId, new(request.OperationId, request.Fingerprint, true));
            return Results.Ok(new
            {
                request.OperationId,
                request.Fingerprint,
                verified.Version,
                verified = true,
                backupHash,
                assignmentFingerprint = await coordinator.AssignmentFingerprintAsync(raceId, token)
            });
        });
        return endpoints;
    }

    private static bool MatchesHold(RaceRepairHoldSnapshot? hold, RaceEntryRepairManifest manifest) =>
        hold is { IsActive: true } && hold.OperationId == manifest.HoldOperationId && hold.Generation == manifest.HoldGeneration;

    private static async Task<IAsyncDisposable> LockAsync(string raceId, RaceEntryRepairInspector inspector,
        RaceWriteCoordinator coordinator, CancellationToken token)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var before = await inspector.InspectAsync(raceId, token);
            var keys = Keys(before.Race);
            var held = await coordinator.AcquireAsync(keys, token);
            try
            {
                if (keys.SetEquals(Keys((await inspector.InspectAsync(raceId, token)).Race))) return held;
            }
            catch { await held.DisposeAsync(); throw; }
            await held.DisposeAsync();
        }
        throw new InvalidOperationException("Race changed while acquiring repair locks.");
    }

    private static HashSet<string> Keys(RaceDetails race) => race.Entries.SelectMany(x =>
        new[] { x.HorseId, x.JockeyId, x.TrainerId }).Append(race.RaceId)
        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToHashSet(StringComparer.Ordinal);

    private static RepairPlan BuildPlan(RaceDetails race, RaceEntryRepairManifest manifest, int version)
    {
        if (manifest.ExpectedVersion != version || manifest.ObservedAt == default
            || manifest.ObservedAt > DateTimeOffset.UtcNow.AddMinutes(5)) throw new ArgumentException("Version or observation time is invalid.");
        if (!Regex.IsMatch(manifest.SourceSnapshotSha256 ?? "", "^[0-9A-Fa-f]{64}$"))
            throw new ArgumentException("Archive and independently verify the official HTML; its SHA-256 is required.");
        if (!Uri.TryCreate(manifest.SourceUrl, UriKind.Absolute, out var url) || url.Scheme != "https"
            || url.Host != "www.jra.go.jp" || url.AbsolutePath != "/JRADB/accessD.html")
            throw new ArgumentException("An official JRA card URL is required.");
        var values = HttpUtility.ParseQueryString(url.Query).GetValues("CNAME");
        var match = Regex.Match(values is { Length: 1 } ? values[0]! : "", @"^pw01dde\d{2}(?<course>\d{2})(?<year>\d{4})(?<meeting>\d{2})(?<day>\d{2})(?<race>\d{2})(?<date>\d{8})/[A-Za-z0-9]+$");
        string[] courses = ["", "札幌", "函館", "福島", "新潟", "東京", "中山", "中京", "京都", "阪神", "小倉"];
        if (!match.Success || !int.TryParse(match.Groups["course"].Value, out var course) || course is < 1 or > 10
            || courses[course] != race.RacecourseCode || match.Groups["date"].Value != race.RaceDate?.ToString("yyyyMMdd")
            || int.Parse(match.Groups["race"].Value) != race.RaceNumber
            || (race.MeetingNumber is not null && int.Parse(match.Groups["meeting"].Value) != race.MeetingNumber)
            || (race.DayNumber is not null && int.Parse(match.Groups["day"].Value) != race.DayNumber))
            throw new ArgumentException("Official card does not identify this race.");
        if (manifest.Horses is null || manifest.Horses.Count == 0 || manifest.Horses.Count != race.Entries.Count)
            throw new ArgumentException("The complete official horse set is required.");
        var entries = new List<EntryDetails>();
        foreach (var horse in manifest.Horses)
        {
            if (!JraSourceIdentity.TryNormalizeHorse(horse.SourceUrl, out _) || horse.HorseNumber is < 1 or > 18
                || horse.GateNumber is < 1 or > 8 || string.IsNullOrWhiteSpace(horse.OwnerName))
                throw new ArgumentException("Confirmed number, gate, owner, and source horse identity are required.");
            var id = DeterministicIdGenerator.BuildHorseId("", horse.SourceUrl);
            var old = race.Entries.SingleOrDefault(x => x.HorseId == id)
                ?? throw new ArgumentException("Official horse identity is not in the stored race.");
            entries.Add(old with
            {
                HorseNumber = horse.HorseNumber,
                EntryId = DeterministicIdGenerator.BuildRaceEntryId(race.RaceId, id),
                GateNumber = horse.GateNumber,
                OwnerName = horse.OwnerName
            });
        }
        if (entries.Select(x => x.HorseId).Distinct().Count() != entries.Count
            || entries.Select(x => x.HorseNumber).Distinct().Count() != entries.Count)
            throw new ArgumentException("Horse identities and numbers must be one-to-one.");
        var ordered = entries.OrderBy(x => x.HorseNumber).ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            race.RaceId,
            version,
            manifest.SourceUrl,
            manifest.ObservedAt,
            manifest.GradeCode,
            manifest.SourceSnapshotSha256,
            manifest.HoldOperationId,
            manifest.HoldGeneration,
            sources = manifest.Horses.OrderBy(x => x.HorseNumber),
            previous = race.Entries.OrderBy(x => x.HorseNumber),
            entries = ordered
        })));
        return new(fingerprint, ordered);
    }

    private sealed record RepairPlan(string Fingerprint, IReadOnlyList<EntryDetails> Entries);
}
