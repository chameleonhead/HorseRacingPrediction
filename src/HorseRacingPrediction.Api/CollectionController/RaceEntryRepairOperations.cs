using EventFlow;
using EventFlow.EventStores;
using EventFlow.Subscribers;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Commands.Races;
using AppReadModels = HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Mvc;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Api.CollectionController;

public sealed record RaceEntryRepairHorse(string SourceUrl, int HorseNumber, int GateNumber, string OwnerName);
public sealed record RaceEntryRepairManifest(int ExpectedVersion, string SourceUrl, DateTimeOffset ObservedAt,
    string? GradeCode, IReadOnlyList<RaceEntryRepairHorse> Horses, string SourceSnapshotSha256,
    string? HoldOperationId = null, long HoldGeneration = 0);

internal static class RaceEntryRepairOperations
{
    internal static async Task<IResult> GetHoldAsync(string raceId, [FromServices] CollectionPlatformStore collection, CancellationToken token) =>
        Results.Ok(new GetRaceEntryRepairHoldResponse(ToDto(await collection.GetRaceRepairHoldAsync(raceId, token))));

    internal static async Task<IResult> GetFenceAsync(string raceId, [FromServices] RaceEntryRepairInspector inspector,
        [FromServices] RaceWriteCoordinator coordinator, [FromServices] CollectionPlatformStore collection, CancellationToken token)
    {
        await using var held = await LockAsync(raceId, inspector, coordinator, token);
        var hold = await collection.GetRaceRepairHoldAsync(raceId, token);
        if (hold is { IsActive: true } || await coordinator.ReadBarrierAsync(raceId, token) is { Verified: false })
            return Results.Conflict(new { code = "RaceRepairHeld" });
        return Results.Ok(new GetRaceAssignmentFenceResponse(new RaceAssignmentFenceDto(
            hold?.Generation ?? 0, await coordinator.AssignmentFingerprintAsync(raceId, token))));
    }

    internal static async Task<IResult> PutHoldAsync(string raceId, UpdateRaceEntryRepairHoldRequest request,
        [FromServices] RaceEntryRepairInspector inspector, [FromServices] RaceWriteCoordinator coordinator,
        [FromServices] CollectionPlatformStore collection,
        CancellationToken token)
    {
        if (request?.Hold is null) return Results.BadRequest(new { code = "InvalidRepairHold", message = "Repair hold payload is required." });
        await using var held = await LockAsync(raceId, inspector, coordinator, token);
        try
        {
            var state = await collection.HoldRaceForRepairAsync(raceId, request.Hold.OperationId, request.Hold.ExpectedGeneration,
                request.Hold.Reason, DateTimeOffset.UtcNow, token, await coordinator.AssignmentFingerprintAsync(raceId, token));
            if (state.IsActive && !state.Definitions.Contains("race-detail"))
            {
                var race = (await inspector.InspectAsync(raceId, token)).Race;
                string[] japanese = ["札幌", "函館", "福島", "新潟", "東京", "中山", "中京", "京都", "阪神", "小倉"];
                string[] english = ["Sapporo", "Hakodate", "Fukushima", "Niigata", "Tokyo", "Nakayama", "Chukyo", "Kyoto", "Hanshin", "Kokura"];
                var course = Array.IndexOf(japanese, race.RacecourseCode);
                if (course < 0 || race.RaceDate is null || race.RaceNumber is null)
                    return Results.Conflict(new { code = "RepairHoldNeedsCollectionIdentity", hold = state });
                await collection.RequestAsync(new(CollectionResourceType.Race, "JRA", $"{race.RaceDate:yyyyMMdd}:{english[course]}:{race.RaceNumber}"),
                    new("race-detail"), await collection.GetCurrentRevisionAsync(new("race-detail"), token), CollectionReason.Recovery,
                    DateTimeOffset.UtcNow, batchId: "repair-hold:" + request.Hold.OperationId,
                    attributes: new Dictionary<string, string> { ["domainRaceId"] = raceId }, cancellationToken: token);
            }
            return Results.Ok(new UpdateRaceEntryRepairHoldResponse(ToDto(await collection.GetRaceRepairHoldAsync(raceId, token))));
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { code = "InvalidRepairHold", message = ex.Message }); }
        catch (InvalidOperationException) { return Results.Conflict(new { code = "RepairHoldConflict" }); }
    }

    internal static async Task<IResult> PatchHoldAsync(string raceId, ReleaseRaceEntryRepairHoldRequest request,
        [FromServices] RaceEntryRepairInspector inspector, [FromServices] RaceWriteCoordinator coordinator,
        [FromServices] CollectionPlatformStore collection,
        CancellationToken token)
    {
        if (request?.Release is null) return Results.BadRequest(new { code = "InvalidRepairRelease" });
        await using var held = await LockAsync(raceId, inspector, coordinator, token);
        var hold = await collection.GetRaceRepairHoldAsync(raceId, token);
        var release = request.Release;
        if (hold is null || hold.OperationId != release.HoldOperationId || hold.Generation != release.HoldGeneration)
            return Results.Conflict(new { code = "StaleRepairHold" });
        if (hold.IsActive)
        {
            var inspection = await inspector.InspectAsync(raceId, token);
            var barrier = await coordinator.ReadBarrierAsync(raceId, token);
            if (!hold.IsQuiescent || inspection.Blockers.Count != 0 || inspection.Version != release.ExpectedVersion
                || await coordinator.AssignmentFingerprintAsync(raceId, token) != release.AssignmentFingerprint)
                return Results.Conflict(new { code = "RepairReleaseNotVerified" });
            if (release.CancelRepair ? inspection.PreviousRepair is not null || barrier is not null
                : barrier is not { Verified: true } || barrier.OperationId != release.RepairOperationId
                    || barrier.Fingerprint != release.RepairFingerprint || inspection.PreviousRepair?.OperationId != release.RepairOperationId)
                return Results.Conflict(new { code = "RepairReleaseNotVerified" });
        }
        try
        {
            return Results.Ok(new ReleaseRaceEntryRepairHoldResponse(ToDto(await collection.ReleaseRaceRepairHoldAsync(raceId,
            release.HoldOperationId, release.HoldGeneration, release.OperationId, release.AssignmentFingerprint,
            DateTimeOffset.UtcNow, token))));
        }
        catch (ArgumentException) { return Results.BadRequest(new { code = "InvalidRepairRelease" }); }
        catch (InvalidOperationException) { return Results.Conflict(new { code = "RepairReleaseConflict" }); }
    }

    internal static async Task<IResult> GetInspectionAsync(string raceId, [FromServices] RaceEntryRepairInspector inspector,
        [FromServices] RaceWriteCoordinator coordinator, CancellationToken token)
    {
        await using var held = await LockAsync(raceId, inspector, coordinator, token);
        return Results.Ok(new GetRaceEntryRepairResponse(ToDto(await inspector.InspectAsync(raceId, token))));
    }

    internal static async Task<IResult> PreviewAsync(string raceId, PreviewRaceEntryRepairRequest request,
        [FromServices] RaceEntryRepairInspector inspector, [FromServices] RaceWriteCoordinator coordinator,
        [FromServices] CollectionPlatformStore collection,
        CancellationToken token)
    {
        await using var held = await LockAsync(raceId, inspector, coordinator, token);
        var inspection = await inspector.InspectAsync(raceId, token);
        RaceEntryRepairManifest manifest;
        try
        {
            manifest = ToInternal(request.Manifest);
            var plan = BuildPlan(inspection.Race, manifest, inspection.Version);
            var blockers = inspection.Blockers.ToList();
            if (inspection.PreviousRepair is not null) blockers.Add("AlreadyRepaired");
            if (await coordinator.ReadBarrierAsync(raceId, token) is not null) blockers.Add("RepairBarrierExists");
            var hold = await collection.GetRaceRepairHoldAsync(raceId, token);
            if (!MatchesHold(hold, manifest)) blockers.Add("RepairHoldRequired");
            else if (!hold!.IsQuiescent) blockers.Add("RepairHoldNotQuiescent");
            return Results.Ok(new PreviewRaceEntryRepairResponse(new RaceEntryRepairPreviewDto(
                blockers.Count == 0, plan.Fingerprint, inspection.Version,
                inspection.Race.Entries.Select(ToDto).ToArray(), plan.Entries.Select(ToDto).ToArray(), manifest.GradeCode,
                inspection.ReferenceCounts, blockers, ToDto(hold), ToDto(manifest),
                "OperatorProvidedSnapshotRequiresIndependentReview")));
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { code = "InvalidRepairManifest", message = ex.Message }); }
    }

    internal static async Task<IResult> ApplyAsync(string raceId, HorseRacingPrediction.Contracts.Races.ApplyRaceEntryRepairRequest request,
        [FromServices] RaceEntryRepairInspector inspector, [FromServices] RaceWriteCoordinator coordinator,
        [FromServices] CollectionPlatformStore collection, [FromServices] ICommandBus commands,
        [FromServices] IEventStore events, [FromServices] IDomainEventPublisher publisher, CancellationToken token)
    {
        if (!Guid.TryParse(request.OperationId, out _)) return Results.BadRequest(new { code = "InvalidOperationId" });
        await using var held = await LockAsync(raceId, inspector, coordinator, token);
        var inspection = await inspector.InspectAsync(raceId, token);
        var previous = inspection.PreviousRepair;
        var basis = previous is null ? inspection.Race : inspection.Race with { Entries = previous.PreviousEntries };
        RepairPlan plan;
        RaceEntryRepairManifest manifest;
        try
        {
            manifest = ToInternal(request.Manifest);
            plan = BuildPlan(basis, manifest, previous is null ? inspection.Version : manifest.ExpectedVersion);
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { code = "InvalidRepairManifest", message = ex.Message }); }
        if (plan.Fingerprint != request.Fingerprint) return Results.Conflict(new { code = "StaleRepairPreview" });
        var barrier = await coordinator.ReadBarrierAsync(raceId, token);
        if (barrier is not null && (barrier.OperationId != request.OperationId || barrier.Fingerprint != request.Fingerprint))
            return Results.Conflict(new { code = "DifferentRepairPending" });
        if (previous is not null && (previous.OperationId != request.OperationId || previous.Fingerprint != request.Fingerprint))
            return Results.Conflict(new { code = "AlreadyRepaired" });
        var hold = await collection.GetRaceRepairHoldAsync(raceId, token);
        if (!MatchesHold(hold, manifest)) return Results.Conflict(new { code = "RepairHoldRequired" });
        if (!hold!.IsQuiescent) return Results.Conflict(new { code = "RepairHoldNotQuiescent" });
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
                request.OperationId, request.Fingerprint, manifest.SourceUrl, manifest.ObservedAt,
                plan.Entries, manifest.GradeCode, JsonSerializer.Serialize(manifest)), token);
            if (!execution.IsSuccess) return Results.Conflict(new { code = "RepairCommandFailed" });
        }
        else
        {
            var stream = await events.LoadEventsAsync<RaceAggregate, RaceId>(new RaceId(raceId), token);
            await publisher.PublishAsync(stream.Where(x => x.GetAggregateEvent() is RaceEntryAssignmentsRepaired).ToArray(), token);
        }
        var verified = await inspector.InspectAsync(raceId, token);
        if (verified.PreviousRepair?.OperationId != request.OperationId || verified.PreviousRepair.Fingerprint != request.Fingerprint
            || verified.Version != manifest.ExpectedVersion + 1
            || JsonSerializer.Serialize(verified.Race.Entries.OrderBy(x => x.HorseNumber)) != JsonSerializer.Serialize(plan.Entries))
            return Results.Conflict(new { code = "RepairEventNotVerified" });
        if (verified.Blockers.Count != 0) return Results.Conflict(new { code = "RepairProjectionPending", blockers = verified.Blockers });
        coordinator.WriteBarrier(raceId, new(request.OperationId, request.Fingerprint, true));
        return Results.Ok(new ApplyRaceEntryRepairResponse(request.OperationId, request.Fingerprint,
            verified.Version, true, backupHash, await coordinator.AssignmentFingerprintAsync(raceId, token)));
    }

    private static RaceEntryRepairManifest ToInternal(RaceEntryRepairManifestInputDto? manifest)
    {
        if (manifest is null) throw new ArgumentException("Repair manifest is required.", nameof(manifest));
        return new(manifest.ExpectedVersion, manifest.SourceUrl, manifest.ObservedAt, manifest.GradeCode,
            manifest.Horses is null ? null! : manifest.Horses.Select(horse => new RaceEntryRepairHorse(
                horse.SourceUrl, horse.HorseNumber, horse.GateNumber, horse.OwnerName)).ToArray(),
            manifest.SourceSnapshotSha256, manifest.HoldOperationId, manifest.HoldGeneration);
    }

    private static RaceEntryRepairManifestDto ToDto(RaceEntryRepairManifest manifest) =>
        new(manifest.ExpectedVersion, manifest.SourceUrl, manifest.ObservedAt, manifest.GradeCode,
            manifest.Horses.Select(horse => new RaceEntryRepairHorseDto(horse.SourceUrl, horse.HorseNumber,
                horse.GateNumber, horse.OwnerName)).ToArray(), manifest.SourceSnapshotSha256,
            manifest.HoldOperationId, manifest.HoldGeneration);

    private static RaceRepairHoldDto? ToDto(RaceRepairHoldSnapshot? hold) => hold is null ? null :
        new(hold.RaceId, hold.OperationId, hold.Generation, hold.Reason, hold.CreatedAt, hold.ReleasedAt,
            hold.AssignmentFingerprint, hold.ReadyTasks, hold.RunningTasks, hold.Blockers, hold.RequiredRevision,
            hold.UnresolvedLeases, hold.Aliases, hold.Definitions);

    private static RaceEntryRepairInspectionDto ToDto(RaceRepairInspection inspection) => new(
        ToDto(inspection.Race), inspection.Version, inspection.Blockers, inspection.ReferenceCounts,
        inspection.PreviousRepair is null ? null : ToDto(inspection.PreviousRepair));

    private static RaceEntryAssignmentsRepairedDto ToDto(RaceEntryAssignmentsRepaired repair) => new(
        repair.OperationId, repair.Fingerprint, repair.SourceUrl, repair.ObservedAt, repair.SourceEvidenceJson,
        repair.PreviousEntries.Select(ToDto).ToArray(), repair.Entries.Select(ToDto).ToArray(), repair.RaceDate,
        repair.RacecourseCode, repair.GradeCode, repair.SurfaceCode, repair.DistanceMeters, repair.DirectionCode);

    private static RaceRepairDetailsDto ToDto(RaceDetails race) => new(
        race.RaceId, race.RaceDate, race.RacecourseCode, race.RaceNumber, race.RaceName,
        (HorseRacingPrediction.Contracts.Races.RaceStatus)race.Status, race.MeetingNumber, race.DayNumber,
        race.GradeCode, race.SurfaceCode, race.DistanceMeters, race.DirectionCode, race.EntryCount,
        race.Entries.Select(ToDto).ToArray(), race.WeatherObservations.Select(x => new RaceWeatherObservationDto(
            x.ObservationTime, x.WeatherCode, x.WeatherText, x.TemperatureCelsius, x.HumidityPercent,
            x.WindDirectionCode, x.WindSpeedMeterPerSecond)).ToArray(),
        race.TrackConditionObservations.Select(x => new RaceTrackConditionDto(x.ObservationTime,
            x.TurfConditionCode, x.DirtConditionCode, x.GoingDescriptionText)).ToArray(),
        race.WinningHorseName, race.WinningHorseId, race.StewardReportText, race.ResultDeclaredAt,
        race.EntryResults.Select(x => new RaceRepairEntryResultDto(x.EntryId, x.FinishPosition, x.OfficialTime,
            x.MarginText, x.LastThreeFurlongTime, x.AbnormalResultCode, x.PrizeMoney, x.CornerPositions,
            x.Popularity, x.OriginalFinishPosition, x.IsDeadHeat, x.Average1F, x.AdditionalPrizeMoney)).ToArray(),
        ToDto(race.PayoutResult), race.StartTime, race.OverallPaceText, race.CornerPassagesText,
        race.CourseLayout, race.ReplacementRaceId);

    private static RaceRepairEntryDto ToDto(EntryDetails entry) => new(entry.EntryId, entry.HorseId,
        entry.HorseNumber, entry.JockeyId, entry.TrainerId, entry.GateNumber, entry.AssignedWeight,
        entry.SexCode, entry.Age, entry.DeclaredWeight, entry.DeclaredWeightDiff, entry.RunningStyleCode,
        entry.OwnerName, (HorseRacingPrediction.Contracts.Races.RaceEntryParticipationStatus?)entry.ParticipationStatus);

    private static RacePayoutResultDto? ToDto(PayoutResultDetails? payout) => payout is null ? null : new(
        payout.DeclaredAt, payout.WinPayouts.Select(ToDto).ToArray(), payout.PlacePayouts.Select(ToDto).ToArray(),
        payout.QuinellaPayouts.Select(ToDto).ToArray(), payout.ExactaPayouts.Select(ToDto).ToArray(),
        payout.TrifectaPayouts.Select(ToDto).ToArray(), payout.BracketQuinellaPayouts?.Select(ToDto).ToArray(),
        payout.WidePayouts?.Select(ToDto).ToArray(), payout.TrioPayouts?.Select(ToDto).ToArray());

    private static RacePayoutResultDto? ToDto(AppReadModels.PayoutResultSnapshot? payout) => payout is null ? null : new(
        payout.DeclaredAt, payout.WinPayouts.Select(ToDto).ToArray(), payout.PlacePayouts.Select(ToDto).ToArray(),
        payout.QuinellaPayouts.Select(ToDto).ToArray(), payout.ExactaPayouts.Select(ToDto).ToArray(),
        payout.TrifectaPayouts.Select(ToDto).ToArray(), payout.BracketQuinellaPayouts?.Select(ToDto).ToArray(),
        payout.WidePayouts?.Select(ToDto).ToArray(), payout.TrioPayouts?.Select(ToDto).ToArray());

    private static RacePayoutEntryDto ToDto(PayoutEntry payout) => new(payout.Combination, payout.Amount);
    private static RacePayoutEntryDto ToDto(AppReadModels.PayoutEntrySnapshot payout) => new(payout.Combination, payout.Amount);

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
            try { if (keys.SetEquals(Keys((await inspector.InspectAsync(raceId, token)).Race))) return held; }
            catch { await held.DisposeAsync(); throw; }
            await held.DisposeAsync();
        }
        throw new InvalidOperationException("Race changed while acquiring repair locks.");
    }

    private static HashSet<string> Keys(RaceDetails race) => race.Entries.SelectMany(x => new[] { x.HorseId, x.JockeyId, x.TrainerId })
        .Append(race.RaceId).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToHashSet(StringComparer.Ordinal);

    private static RepairPlan BuildPlan(RaceDetails race, RaceEntryRepairManifest manifest, int version)
    {
        if (manifest.ExpectedVersion != version || manifest.ObservedAt == default || manifest.ObservedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new ArgumentException("Version or observation time is invalid.");
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
            var old = race.Entries.SingleOrDefault(x => x.HorseId == id) ?? throw new ArgumentException("Official horse identity is not in the stored race.");
            entries.Add(old with
            {
                HorseNumber = horse.HorseNumber,
                EntryId = DeterministicIdGenerator.BuildRaceEntryId(race.RaceId, id),
                GateNumber = horse.GateNumber,
                OwnerName = horse.OwnerName
            });
        }
        if (entries.Select(x => x.HorseId).Distinct().Count() != entries.Count || entries.Select(x => x.HorseNumber).Distinct().Count() != entries.Count)
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
