using EventFlow;
using EventFlow.EntityFramework;
using EventFlow.Queries;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Jockeys;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Domain.Trainers;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shared = HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Api;

public static partial class EndpointExtensions
{
    private static async Task<IResult> ApplyCollectedRaceResultBulkAsync(
        Shared.DeclareRaceResultBulkRequest request,
        ICommandBus commandBus,
        IQueryProcessor queryProcessor,
        IDbContextProvider<EventStoreDbContext> dbContextProvider,
        CollectionPlatformStore collectionStore,
        CancellationToken cancellationToken)
    {
        if (request.RefreshExistingData)
            return await RefreshCollectedRaceAsync(request, commandBus, queryProcessor, dbContextProvider, cancellationToken);

        var raceIdValue = DeterministicIdGenerator.BuildRaceId(
            request.RaceDate, request.RacecourseCode, request.RaceNumber);
        var raceId = new RaceId(raceIdValue);
        var existing = await queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<RacePredictionContextReadModel>(raceIdValue), cancellationToken).ConfigureAwait(false);
        if (existing is not null && string.IsNullOrEmpty(existing.RaceId)) existing = null;

        var errors = new List<string>();
        var outcomes = new List<Shared.DeclareRaceResultBulkItemOutcome>();
        var accepted = new List<(Shared.RaceResultEntryBulkDto Source, EntryDetails Entry, EntryResultDetails Result,
            string HorseName, string? JockeyName, string? TrainerName)>();
        var seenNumbers = new HashSet<int>();
        var existingEntryIds = (existing?.Entries ?? []).Select(item => item.EntryId).ToHashSet(StringComparer.Ordinal);

        foreach (var item in request.Entries ?? [])
        {
            var key = $"HorseNumber={item.HorseNumber}";
            if (item.HorseNumber <= 0 || !seenNumbers.Add(item.HorseNumber))
            {
                const string message = "HorseNumber must be positive and unique.";
                errors.Add($"着順記録エラー: {key} — {message}");
                outcomes.Add(new("Entry", key, "Rejected", "InvalidHorseNumber", message));
                continue;
            }

            var entryId = DeterministicIdGenerator.BuildRaceEntryId(raceIdValue, item.HorseNumber);
            var isExistingEntry = existingEntryIds.Contains(entryId);
            if (!isExistingEntry && string.IsNullOrWhiteSpace(item.HorseName))
            {
                const string message = "HorseName is required when the race entry does not exist.";
                errors.Add($"出馬表登録エラー: {key} — {message}");
                outcomes.Add(new("Entry", key, "Rejected", "MissingHorseName", message));
                continue;
            }

            var horseName = item.HorseName?.Trim() ?? entryId;
            var canonicalHorseName = Shared.JraSubjectNameNormalizer.CanonicalizeDisplayName("Horse", horseName);
            var canonicalJockeyName = string.IsNullOrWhiteSpace(item.JockeyName) ? null
                : Shared.JraSubjectNameNormalizer.CanonicalizeDisplayName("Jockey", item.JockeyName);
            var canonicalTrainerName = string.IsNullOrWhiteSpace(item.TrainerName) ? null
                : Shared.JraSubjectNameNormalizer.CanonicalizeDisplayName("Trainer", item.TrainerName);
            var horseId = isExistingEntry
                ? existing!.Entries.Single(entry => entry.EntryId == entryId).HorseId
                : DeterministicIdGenerator.BuildHorseId(canonicalHorseName, item.HorseSourceIdentity);
            var jockeyId = string.IsNullOrWhiteSpace(canonicalJockeyName) ? null
                : DeterministicIdGenerator.BuildEntityId("jockey",
                    DeterministicIdGenerator.NormalizeKey(canonicalJockeyName));
            var trainerId = string.IsNullOrWhiteSpace(canonicalTrainerName) ? null
                : DeterministicIdGenerator.BuildEntityId("trainer",
                    DeterministicIdGenerator.NormalizeKey(canonicalTrainerName));
            var entry = new EntryDetails(entryId, horseId, item.HorseNumber, jockeyId, trainerId,
                item.GateNumber, item.AssignedWeight, item.SexCode, item.Age, item.BodyWeight,
                item.BodyWeightChange, null, item.OwnerName);
            var result = new EntryResultDetails(entryId, item.FinishPosition, item.OfficialTime,
                item.MarginText, item.LastThreeFurlongTime, item.AbnormalResultCode, item.PrizeMoney,
                item.CornerPositions, item.Popularity, item.OriginalFinishPosition, item.IsDeadHeat,
                item.Average1F, item.AdditionalPrizeMoney);
            accepted.Add((item, entry, result, canonicalHorseName, canonicalJockeyName, canonicalTrainerName));
            outcomes.Add(new("Entry", key, "Accepted"));
        }

        var gradeCode = ResolveCollectedGradeCode(request.GradeCode, request.RaceName, existing?.RaceName);
        var data = new BulkRaceResultData(
            request.RaceDate, request.RacecourseCode, request.RaceNumber, request.RaceName,
            request.EntryCount, gradeCode, request.SurfaceCode, request.DistanceMeters, request.DirectionCode,
            accepted.Where(item => !existingEntryIds.Contains(item.Entry.EntryId)).Select(item => item.Entry).ToArray(),
            request.IsRaceCard ? [] : accepted.Select(item => item.Result).ToArray(), request.WinningHorseName,
            string.IsNullOrWhiteSpace(request.WinningHorseName)
                ? null
                : request.DeclaredAt ?? Shared.Time.JstTime.Now(),
            request.Payouts is null ? null : new PayoutResultDetails(request.Payouts.DeclaredAt,
                ToPayoutEntries(request.Payouts.WinPayouts), ToPayoutEntries(request.Payouts.PlacePayouts),
                ToPayoutEntries(request.Payouts.QuinellaPayouts), ToPayoutEntries(request.Payouts.ExactaPayouts),
                ToPayoutEntries(request.Payouts.TrifectaPayouts)),
            request.Weather is null ? null : new WeatherObservationDetails(request.Weather.ObservationTime,
                request.Weather.WeatherCode, request.Weather.WeatherText, request.Weather.TemperatureCelsius,
                request.Weather.HumidityPercent, request.Weather.WindDirectionCode, request.Weather.WindSpeedMeterPerSecond),
            request.TrackCondition is null ? null : new TrackConditionObservationDetails(
                request.TrackCondition.ObservationTime, request.TrackCondition.TurfConditionCode,
                request.TrackCondition.DirtConditionCode, request.TrackCondition.GoingDescriptionText),
            request.StewardReportText);

        try
        {
            ValidateCollectedRaceResultBulk(data, existing);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            errors.Add($"レース一括登録エラー: {ex.Message}");
            MarkAcceptedOutcomesFailed(outcomes, "RaceBulkValidationFailed", ex.Message);
            return Results.Ok(new Shared.DeclareRaceResultBulkResponse(raceIdValue, errors, outcomes));
        }

        try
        {
            // Existing race entries can predate related-subject creation (or refer to a
            // canonical source-identity Horse that has not been materialized yet). Replays
            // must heal that missing subject before the profile collection task runs.
            await EnsureRelatedSubjectsBulkAsync(accepted,
                commandBus, dbContextProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            errors.Add($"関連主体登録エラー: {ex.Message}");
            MarkAcceptedOutcomesFailed(outcomes, "RelatedSubjectUpsertFailed", ex.Message);
            return Results.Ok(new Shared.DeclareRaceResultBulkResponse(raceIdValue, errors, outcomes));
        }

        try
        {
            var result = await commandBus.PublishAsync(new ApplyBulkRaceResultCommand(raceId, data), cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                const string message = "Command execution failed.";
                errors.Add($"レース一括登録エラー: {message}");
                MarkAcceptedOutcomesFailed(outcomes, "RaceBulkCommandFailed", message);
            }
            else if (request.IsRaceCard)
            {
                var jobOutcomes = await RequestSubjectProfileJobsAsync(raceIdValue, request.RaceDate,
                    accepted, collectionStore, dbContextProvider, cancellationToken).ConfigureAwait(false);
                foreach (var rejected in jobOutcomes.Where(item => item.Status == "Rejected"))
                    errors.Add($"主体識別情報の補正候補として記録: {rejected.ItemKey} — {rejected.ErrorCode}: {rejected.Message}");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            errors.Add($"レース一括登録エラー: {ex.Message}");
            MarkAcceptedOutcomesFailed(outcomes, "RaceBulkCommandFailed", ex.Message);
        }

        return Results.Ok(new Shared.DeclareRaceResultBulkResponse(raceIdValue, errors, outcomes));
    }

    private static async Task<IReadOnlyList<CollectionRequestBatchOutcome>> RequestSubjectProfileJobsAsync(
        string raceId, DateOnly raceDate,
        IReadOnlyList<(Shared.RaceResultEntryBulkDto Source, EntryDetails Entry, EntryResultDetails Result,
            string HorseName, string? JockeyName, string? TrainerName)> entries,
        CollectionPlatformStore store, IDbContextProvider<EventStoreDbContext> dbContextProvider,
        CancellationToken cancellationToken)
    {
        var today = Shared.Time.JstTime.Today();
        var realtime = raceDate >= today && raceDate <= today.AddDays(7);
        var lane = realtime ? CollectionLane.Realtime : CollectionLane.Normal;
        var priority = realtime ? (int)CollectionPriority.High : (int)CollectionPriority.Low;
        var candidates = entries.SelectMany(item => new[]
            {
                new SubjectJob(ResourceType.Horse, item.Entry.HorseId, item.HorseName,
                    item.Source.HorseSourceIdentity),
                new SubjectJob(ResourceType.Jockey, item.Entry.JockeyId, item.JockeyName,
                    item.Source.JockeyProfileUrl),
                new SubjectJob(ResourceType.Trainer, item.Entry.TrainerId, item.TrainerName,
                    item.Source.TrainerProfileUrl),
                new SubjectJob(ResourceType.Owner,
                    string.IsNullOrWhiteSpace(item.Source.OwnerName) ? null :
                        DeterministicIdGenerator.BuildEntityId("owner",
                            DeterministicIdGenerator.NormalizeKey(item.Source.OwnerName)),
                    item.Source.OwnerName, null),
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
            .DistinctBy(item => (item.Type, item.Id))
            .OrderBy(item => item.Type).ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0) return [];

        using var db = dbContextProvider.CreateContext();
        var race = await db.Set<RacePredictionContextReadModel>().AsNoTracking()
            .SingleAsync(item => item.RaceId == raceId, cancellationToken).ConfigureAwait(false);
        var horseIds = candidates.Where(x => x.Type == ResourceType.Horse).Select(x => x.Id!).ToArray();
        var jockeyIds = candidates.Where(x => x.Type == ResourceType.Jockey).Select(x => x.Id!).ToArray();
        var trainerIds = candidates.Where(x => x.Type == ResourceType.Trainer).Select(x => x.Id!).ToArray();
        var horses = await db.Set<HorseReadModel>().AsNoTracking().Where(x => horseIds.Contains(x.HorseId))
            .ToDictionaryAsync(x => x.HorseId, cancellationToken).ConfigureAwait(false);
        var jockeys = await db.Set<JockeyReadModel>().AsNoTracking().Where(x => jockeyIds.Contains(x.JockeyId))
            .ToDictionaryAsync(x => x.JockeyId, cancellationToken).ConfigureAwait(false);
        var trainers = await db.Set<TrainerReadModel>().AsNoTracking().Where(x => trainerIds.Contains(x.TrainerId))
            .ToDictionaryAsync(x => x.TrainerId, cancellationToken).ConfigureAwait(false);
        var rejected = new List<CollectionRequestBatchOutcome>();
        var repairIssues = new List<SubjectIdentificationRepairIssue>();
        var ready = candidates.Where(subject =>
        {
            var referenced = subject.Type switch
            {
                ResourceType.Horse => race.Entries.Any(x => x.HorseId == subject.Id),
                ResourceType.Jockey => race.Entries.Any(x => x.JockeyId == subject.Id),
                ResourceType.Trainer => race.Entries.Any(x => x.TrainerId == subject.Id),
                ResourceType.Owner => true,
                _ => false,
            };
            var projectedName = subject.Type switch
            {
                ResourceType.Horse when horses.TryGetValue(subject.Id!, out var horse) => horse.RegisteredName,
                ResourceType.Jockey when jockeys.TryGetValue(subject.Id!, out var jockey) => jockey.DisplayName,
                ResourceType.Trainer when trainers.TryGetValue(subject.Id!, out var trainer) => trainer.DisplayName,
                ResourceType.Owner => subject.Name,
                _ => null,
            };
            var valid = referenced && projectedName is not null
                && string.Equals(Shared.JraSubjectNameNormalizer.NormalizeIdentityName(subject.Type.ToString(), projectedName),
                    Shared.JraSubjectNameNormalizer.NormalizeIdentityName(subject.Type.ToString(), subject.Name!),
                    StringComparison.Ordinal)
                && string.Equals(subject.Id, ExpectedSubjectJobId(subject), StringComparison.Ordinal);
            if (valid || subject.Type == ResourceType.Owner) return true;
            rejected.Add(new($"{subject.Type}:{subject.Id}", "Rejected",
                ErrorCode: "SubjectIdentityRepairRequired",
                Message: "主体投影、名称、またはRaceEntry参照が一致しないためLambdaへ送信しません。"));
            var evidence = $"{raceId}|{subject.Type}|{subject.Id}|{subject.Name}|{subject.SourceIdentity}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(evidence));
            var fingerprint = Convert.ToHexString(hash).ToLowerInvariant();
            repairIssues.Add(new()
            {
                IssueId = new Guid(hash[..16]),
                SubjectType = subject.Type.ToString(),
                SubjectId = subject.Id!,
                SubjectName = subject.Name!,
                DefinitionId = subject.Type switch
                {
                    ResourceType.Horse => "horse-profile",
                    ResourceType.Jockey => "jockey-profile",
                    ResourceType.Trainer => "trainer-profile",
                    _ => string.Empty,
                },
                RequestedByRaceId = raceId,
                SourceIdentity = subject.SourceIdentity,
                SourceUrl = subject.Type is ResourceType.Jockey or ResourceType.Trainer ? subject.SourceIdentity : null,
                ReasonCode = "SubjectIdentityRepairRequired",
                ReasonMessage = "主体投影、名称、決定論的ID、またはRaceEntry参照が一致しません。",
                EvidenceFingerprint = fingerprint,
                Status = "Open",
                CreatedAt = Shared.Time.JstTime.Now(),
            });
            return false;
        }).ToArray();
        if (repairIssues.Count > 0)
        {
            var fingerprints = repairIssues.Select(x => x.EvidenceFingerprint).ToArray();
            var existing = await db.SubjectIdentificationRepairIssues
                .Where(x => fingerprints.Contains(x.EvidenceFingerprint))
                .ToDictionaryAsync(x => x.EvidenceFingerprint, cancellationToken).ConfigureAwait(false);
            foreach (var issue in repairIssues)
            {
                if (existing.TryGetValue(issue.EvidenceFingerprint, out var current))
                {
                    if (current.Status == "Resolved") current.Occurrence++;
                    current.Status = "Open"; current.ResolvedAt = null;
                    current.TargetSubjectId = null; current.RecoveryTaskId = null;
                    current.ReasonMessage = issue.ReasonMessage; current.CreatedAt = issue.CreatedAt;
                }
                else db.SubjectIdentificationRepairIssues.Add(issue);
            }
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                foreach (var issue in repairIssues)
                {
                    var current = await db.SubjectIdentificationRepairIssues.SingleAsync(
                        x => x.EvidenceFingerprint == issue.EvidenceFingerprint, cancellationToken)
                        .ConfigureAwait(false);
                    if (current.Status == "Resolved") current.Occurrence++;
                    current.Status = "Open"; current.ResolvedAt = null;
                    current.TargetSubjectId = null; current.RecoveryTaskId = null;
                    current.ReasonMessage = issue.ReasonMessage; current.CreatedAt = issue.CreatedAt;
                }
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var items = ready.Select(subject =>
        {
            var definition = subject.Type switch
            {
                ResourceType.Horse => (Id: "horse-profile", Revision: 3),
                ResourceType.Jockey => (Id: "jockey-profile", Revision: 3),
                ResourceType.Trainer => (Id: "trainer-profile", Revision: 3),
                ResourceType.Owner => (Id: "owner-identity", Revision: 1),
                _ => throw new InvalidOperationException($"Unsupported subject type: {subject.Type}"),
            };
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = subject.Name!,
                ["requestedByRaceId"] = raceId,
                ["weekendPriorityUntil"] = raceDate.ToString("yyyy-MM-dd"),
                ["discoveredFromType"] = ResourceType.Race.ToString(),
                ["discoveredFromProvider"] = "JRA",
                ["discoveredFromId"] = raceId,
            };
            Uri? explicitUrl = null;
            if (subject.Type == ResourceType.Horse
                && JraSourceIdentity.TryNormalizeHorse(subject.SourceIdentity, out _))
            {
                explicitUrl = JraSourceIdentity.NormalizeHorseUrl(subject.SourceIdentity);
                attributes["sourceIdentity"] = explicitUrl!.AbsoluteUri;
                attributes["sourceUrl"] = explicitUrl.AbsoluteUri;
            }
            else if (subject.Type is ResourceType.Jockey or ResourceType.Trainer
                     && IsAllowedJraProfileUrl(subject.SourceIdentity, subject.Type, out var profileUrl))
            {
                explicitUrl = profileUrl!;
                attributes["sourceUrl"] = explicitUrl.AbsoluteUri;
            }
            return new CollectionRequestBatchItem($"{subject.Type}:{subject.Id}",
                new(subject.Type, "JRA", subject.Id!), new(definition.Id), definition.Revision,
                CollectionReason.Discovery, lane, priority, explicitUrl, raceDate, attributes);
        }).ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            items.Select(item => item.ItemKey))))).ToLowerInvariant()[..24];
        var accepted = items.Length == 0 ? [] : await store.RequestManyAsync(
            $"race-subjects:{raceId}:{fingerprint}", items, Shared.Time.JstTime.Now(), cancellationToken)
            .ConfigureAwait(false);
        return [.. accepted, .. rejected];
    }

    private sealed record SubjectJob(ResourceType Type, string? Id, string? Name, string? SourceIdentity);

    private static string? ExpectedSubjectJobId(SubjectJob subject)
    {
        var canonical = Shared.JraSubjectNameNormalizer.CanonicalizeDisplayName(
            subject.Type.ToString(), subject.Name!);
        return subject.Type switch
        {
            ResourceType.Horse => DeterministicIdGenerator.BuildHorseId(canonical,
                JraSourceIdentity.TryNormalizeHorse(subject.SourceIdentity, out _) ? subject.SourceIdentity : null),
            ResourceType.Jockey => DeterministicIdGenerator.BuildEntityId("jockey",
                DeterministicIdGenerator.NormalizeKey(canonical)),
            ResourceType.Trainer => DeterministicIdGenerator.BuildEntityId("trainer",
                DeterministicIdGenerator.NormalizeKey(canonical)),
            ResourceType.Owner => subject.Id,
            _ => null,
        };
    }

    private static bool IsAllowedJraProfileUrl(string? value, ResourceType type, out Uri? uri)
    {
        uri = Uri.TryCreate(value, UriKind.Absolute, out var parsed) ? parsed : null;
        var path = type == ResourceType.Jockey ? "/JRADB/accessK.html" : "/JRADB/accessC.html";
        return uri is not null && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("www.jra.go.jp", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(uri.Query.TrimStart('?'));
    }

    private static void ValidateCollectedRaceResultBulk(BulkRaceResultData data,
        RacePredictionContextReadModel? existing)
    {
        if (data.RaceDate == default) throw new ArgumentException("Race date is required.");
        if (string.IsNullOrWhiteSpace(data.RacecourseCode)) throw new ArgumentException("Racecourse code is required.");
        if (data.RaceNumber <= 0) throw new ArgumentException("Race number must be positive.");
        if (string.IsNullOrWhiteSpace(data.RaceName)) throw new ArgumentException("Race name is required.");
        if (data.EntryCount is <= 0) throw new ArgumentException("Entry count must be positive when specified.");
        if (!string.IsNullOrWhiteSpace(data.WinningHorseName) && data.DeclaredAt is null)
            throw new ArgumentException("Declared time is required with a winning horse.");
        if (data.Entries.Select(item => item.EntryId).Distinct(StringComparer.Ordinal).Count() != data.Entries.Count
            || data.Entries.Select(item => item.HorseNumber).Distinct().Count() != data.Entries.Count)
            throw new ArgumentException("Incoming race entries must be unique.");
        if (data.EntryResults.Select(item => item.EntryId).Distinct(StringComparer.Ordinal).Count()
            != data.EntryResults.Count)
            throw new ArgumentException("Entry result IDs must be unique.");

        var knownEntryIds = (existing?.Entries ?? []).Select(item => item.EntryId)
            .Concat(data.Entries.Select(item => item.EntryId)).ToHashSet(StringComparer.Ordinal);
        if (data.EntryResults.Any(item => !knownEntryIds.Contains(item.EntryId)))
            throw new ArgumentException("Every entry result must reference a registered or incoming entry.");

        var effectiveStatus = existing?.Status ?? RaceStatus.Draft;
        if (effectiveStatus == RaceStatus.Draft && data.EntryCount is > 0)
            effectiveStatus = RaceStatus.CardPublished;
        if (!string.IsNullOrWhiteSpace(data.WinningHorseName) && effectiveStatus < RaceStatus.ResultDeclared)
            effectiveStatus = RaceStatus.ResultDeclared;
        if (data.Entries.Count > 0 && effectiveStatus == RaceStatus.Draft)
            throw new ArgumentException("Entries require a published race card.");
        if (data.EntryResults.Count > 0 && effectiveStatus < RaceStatus.ResultDeclared)
            throw new ArgumentException("Entry results require a declared race result.");
        if (data.Payouts is not null && effectiveStatus < RaceStatus.ResultDeclared)
            throw new ArgumentException("Payouts require a declared race result.");
    }

    private static IReadOnlyList<PayoutEntry> ToPayoutEntries(IReadOnlyList<Shared.PayoutEntryDto>? values)
        => values?.Select(item => new PayoutEntry(item.Combination, item.Amount)).ToArray() ?? [];

    private static void MarkAcceptedOutcomesFailed(List<Shared.DeclareRaceResultBulkItemOutcome> outcomes,
        string errorCode, string message)
    {
        for (var index = 0; index < outcomes.Count; index++)
            if (outcomes[index].Status == "Accepted")
                outcomes[index] = outcomes[index] with { Status = "Failed", ErrorCode = errorCode, Message = message };
    }

    private static async Task EnsureRelatedSubjectsBulkAsync(
        IEnumerable<(Shared.RaceResultEntryBulkDto Source, EntryDetails Entry, EntryResultDetails Result,
            string HorseName, string? JockeyName, string? TrainerName)> source,
        ICommandBus commandBus,
        IDbContextProvider<EventStoreDbContext> dbContextProvider,
        CancellationToken cancellationToken)
    {
        var items = source.ToArray();
        var horseIds = items.Select(item => item.Entry.HorseId).Distinct(StringComparer.Ordinal).ToArray();
        var jockeyIds = items.Select(item => item.Entry.JockeyId).Where(id => id is not null).Cast<string>()
            .Distinct(StringComparer.Ordinal).ToArray();
        var trainerIds = items.Select(item => item.Entry.TrainerId).Where(id => id is not null).Cast<string>()
            .Distinct(StringComparer.Ordinal).ToArray();
        using var db = dbContextProvider.CreateContext();
        var existingHorses = horseIds.Length == 0 ? [] : await db.Set<HorseReadModel>().AsNoTracking()
            .Where(item => horseIds.Contains(item.HorseId)).Select(item => item.HorseId).ToArrayAsync(cancellationToken);
        var existingJockeys = jockeyIds.Length == 0 ? [] : await db.Set<JockeyReadModel>().AsNoTracking()
            .Where(item => jockeyIds.Contains(item.JockeyId)).Select(item => item.JockeyId).ToArrayAsync(cancellationToken);
        var existingTrainers = trainerIds.Length == 0 ? [] : await db.Set<TrainerReadModel>().AsNoTracking()
            .Where(item => trainerIds.Contains(item.TrainerId)).Select(item => item.TrainerId).ToArrayAsync(cancellationToken);

        var horseSet = existingHorses.ToHashSet(StringComparer.Ordinal);
        foreach (var item in items.DistinctBy(item => item.Entry.HorseId))
            if (horseSet.Add(item.Entry.HorseId))
            {
                var result = await commandBus.PublishAsync(new RegisterHorseCommand(new HorseId(item.Entry.HorseId),
                    item.HorseName, NormalizeDisplayName(item.HorseName), item.Source.SexCode), cancellationToken);
                if (!result.IsSuccess) throw new InvalidOperationException("Horse registration command failed.");
            }

        var jockeySet = existingJockeys.ToHashSet(StringComparer.Ordinal);
        foreach (var item in items.Where(item => item.Entry.JockeyId is not null).DistinctBy(item => item.Entry.JockeyId))
            if (jockeySet.Add(item.Entry.JockeyId!))
            {
                var result = await commandBus.PublishAsync(new RegisterJockeyCommand(new JockeyId(item.Entry.JockeyId!),
                    item.JockeyName!, NormalizeDisplayName(item.JockeyName!), null), cancellationToken);
                if (!result.IsSuccess) throw new InvalidOperationException("Jockey registration command failed.");
            }

        var trainerSet = existingTrainers.ToHashSet(StringComparer.Ordinal);
        foreach (var item in items.Where(item => item.Entry.TrainerId is not null).DistinctBy(item => item.Entry.TrainerId))
            if (trainerSet.Add(item.Entry.TrainerId!))
            {
                var result = await commandBus.PublishAsync(new RegisterTrainerCommand(new TrainerId(item.Entry.TrainerId!),
                    item.TrainerName!, NormalizeDisplayName(item.TrainerName!), null), cancellationToken);
                if (!result.IsSuccess) throw new InvalidOperationException("Trainer registration command failed.");
            }
    }
}
