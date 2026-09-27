using EventFlow;
using EventFlow.EntityFramework;
using EventFlow.Queries;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Contracts.Time;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Jockeys;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Domain.Trainers;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using static HorseRacingPrediction.Api.Endpoints.Races.RaceEndpointMappings;

namespace HorseRacingPrediction.Api.Endpoints.Races;

internal static partial class RaceResultBulkService
{
    internal static async Task<IResult> ApplyCollectedRaceResultBulkAsync(
        DeclareRaceResultBulkRequest request,
        ICommandBus commandBus,
        IQueryProcessor queryProcessor,
        IDbContextProvider<EventStoreDbContext> dbContextProvider,
        CollectionPlatformStore collectionStore,
        CancellationToken cancellationToken)
    {
        if (request.RefreshExistingData)
            return await RefreshCollectedRaceAsync(request, commandBus, queryProcessor, dbContextProvider, cancellationToken);

        using var identityDb = dbContextProvider.CreateContext();
        string raceIdValue;
        Dictionary<RaceResultEntryBulkDto, string> horseIdentities;
        try
        {
            raceIdValue = await CollectionIdentityResolver.RaceAsync(identityDb, request.RaceDate, request.RacecourseCode, request.RaceNumber, cancellationToken);
        }
        catch (InvalidOperationException ex) { return Results.Conflict(new { code = ex.Message }); }
        try { horseIdentities = await ResolveCollectedHorseIdentitiesAsync(request, identityDb, cancellationToken); }
        catch (InvalidOperationException ex) { return CollectedIdentityRejection(raceIdValue, ex.Message); }
        var raceId = new RaceId(raceIdValue);
        var existing = await queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<RacePredictionContextReadModel>(raceIdValue), cancellationToken).ConfigureAwait(false);
        if (existing is not null && string.IsNullOrEmpty(existing.RaceId)) existing = null;

        var identityFailure = ValidateCollectedEntryIdentities(request, existing, raceIdValue, horseIdentities);
        if (identityFailure is not null) return identityFailure;

        var errors = new List<string>();
        var relatedErrors = new List<string>();
        var outcomes = new List<DeclareRaceResultBulkItemOutcome>();
        var accepted = new List<(RaceResultEntryBulkDto Source, EntryDetails Entry, EntryResultDetails Result,
            string HorseName, string? JockeyName, string? TrainerName)>();
        var seenNumbers = new HashSet<int>();

        foreach (var item in request.Entries ?? [])
        {
            var key = $"HorseNumber={item.HorseNumber}";
            if (item.HorseNumber is <= 0 || (!request.IsRaceCard && item.HorseNumber is null)
                || (item.HorseNumber is { } number && !seenNumbers.Add(number)))
            {
                const string message = "HorseNumber must be positive and unique.";
                errors.Add($"着順記録エラー: {key} — {message}");
                outcomes.Add(new("Entry", key, "Rejected", "InvalidHorseNumber", message));
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.HorseName))
            {
                const string message = "HorseName is required when the race entry does not exist.";
                errors.Add($"出馬表登録エラー: {key} — {message}");
                outcomes.Add(new("Entry", key, "Rejected", "MissingHorseName", message));
                continue;
            }

            var horseName = item.HorseName!.Trim();
            var canonicalHorseName = JraSubjectNameNormalizer.CanonicalizeDisplayName("Horse", horseName);
            var canonicalJockeyName = string.IsNullOrWhiteSpace(item.JockeyName) ? null
                : JraSubjectNameNormalizer.CanonicalizeDisplayName("Jockey", item.JockeyName);
            var canonicalTrainerName = string.IsNullOrWhiteSpace(item.TrainerName) ? null
                : JraSubjectNameNormalizer.CanonicalizeDisplayName("Trainer", item.TrainerName);
            var horseId = horseIdentities[item];
            var entryId = existing?.Entries.SingleOrDefault(x => x.HorseId == horseId)?.EntryId
                ?? DeterministicIdGenerator.BuildRaceEntryId(raceIdValue, horseId);
            var jockeyId = string.IsNullOrWhiteSpace(canonicalJockeyName) ? null
                : DeterministicIdGenerator.BuildEntityId("jockey",
                    DeterministicIdGenerator.NormalizeKey(canonicalJockeyName));
            var trainerId = string.IsNullOrWhiteSpace(canonicalTrainerName) ? null
                : DeterministicIdGenerator.BuildEntityId("trainer",
                    DeterministicIdGenerator.NormalizeKey(canonicalTrainerName));
            var entry = new EntryDetails(entryId, horseId, item.HorseNumber, jockeyId, trainerId,
                item.GateNumber, item.AssignedWeight, item.SexCode, item.Age, item.BodyWeight,
                item.BodyWeightChange, null, item.OwnerName, (Domain.Races.RaceEntryParticipationStatus?)item.ParticipationStatus);
            var result = new EntryResultDetails(entryId, item.FinishPosition, item.OfficialTime,
                item.MarginText, item.LastThreeFurlongTime, item.AbnormalResultCode, item.PrizeMoney,
                item.CornerPositions, item.Popularity, item.OriginalFinishPosition, item.IsDeadHeat,
                item.Average1F, item.AdditionalPrizeMoney);
            accepted.Add((item, entry, result, canonicalHorseName, canonicalJockeyName, canonicalTrainerName));
            outcomes.Add(new("Entry", key, "Accepted"));
        }

        if (request.IsRaceCard && HasRaceResultEvidence(request))
        {
            const string message = "Race-card requests must not contain result data.";
            errors.Add($"出馬表登録エラー: {message}");
            MarkAcceptedOutcomesFailed(outcomes, "RaceCardContainsResultData", message);
            return Results.Ok(new DeclareRaceResultBulkResponse(raceIdValue, errors, outcomes));
        }

        var gradeCode = ResolveCollectedGradeCode(request.GradeCode, request.RaceName, existing?.RaceName);
        var data = new BulkRaceResultData(
            request.RaceDate, request.RacecourseCode, request.RaceNumber, request.RaceName,
            request.EntryCount, gradeCode, request.SurfaceCode, request.DistanceMeters, request.DirectionCode,
            accepted.Select(item => item.Entry).ToArray(),
            request.IsRaceCard ? [] : accepted.Select(item => item.Result).ToArray(), request.WinningHorseName,
            string.IsNullOrWhiteSpace(request.WinningHorseName)
                ? null
                : request.DeclaredAt ?? JstTime.Now(),
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

        var corePersisted = false;
        try
        {
            ValidateCollectedRaceResultBulk(data, existing);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            errors.Add($"レース一括登録エラー: {ex.Message}");
            MarkAcceptedOutcomesFailed(outcomes, "RaceBulkValidationFailed", ex.Message);
            return Results.Ok(new DeclareRaceResultBulkResponse(raceIdValue, errors, outcomes));
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
            return Results.Ok(new DeclareRaceResultBulkResponse(raceIdValue, errors, outcomes));
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
                corePersisted = true;
                try
                {
                    var jobOutcomes = await RequestSubjectProfileJobsAsync(raceIdValue, request.RaceDate,
                        accepted, collectionStore, dbContextProvider, cancellationToken).ConfigureAwait(false);
                    foreach (var rejected in jobOutcomes.Where(item => item.Status == "Rejected"))
                    {
                        var message = $"主体識別情報の補正候補として記録: {rejected.ItemKey} — {rejected.ErrorCode}: {rejected.Message}";
                        errors.Add(message);
                        relatedErrors.Add(message);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    var message = $"関連主体収集要求エラー: {ex.Message}";
                    errors.Add(message);
                    relatedErrors.Add(message);
                }
            }
            else if (result.IsSuccess) corePersisted = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            errors.Add($"レース一括登録エラー: {ex.Message}");
            MarkAcceptedOutcomesFailed(outcomes, "RaceBulkCommandFailed", ex.Message);
        }

        return Results.Ok(new DeclareRaceResultBulkResponse(raceIdValue, errors, outcomes,
            corePersisted, relatedErrors));
    }

    internal static bool HasRaceResultEvidence(DeclareRaceResultBulkRequest request)
        => !string.IsNullOrWhiteSpace(request.WinningHorseName)
           || request.DeclaredAt is not null
           || request.Payouts is not null
           || (request.Entries ?? []).Any(item => item.FinishPosition is not null
               || !string.IsNullOrWhiteSpace(item.OfficialTime)
               || !string.IsNullOrWhiteSpace(item.MarginText)
               || !string.IsNullOrWhiteSpace(item.LastThreeFurlongTime)
               || !string.IsNullOrWhiteSpace(item.AbnormalResultCode)
               || item.PrizeMoney is not null
               || item.Popularity is not null
               || item.OriginalFinishPosition is not null
               || item.IsDeadHeat
               || !string.IsNullOrWhiteSpace(item.CornerPositions)
               || item.Average1F is not null
               || item.AdditionalPrizeMoney is not null);

    internal static async Task<IReadOnlyList<CollectionRequestBatchOutcome>> RequestSubjectProfileJobsAsync(
        string raceId, DateOnly raceDate,
        IReadOnlyList<(RaceResultEntryBulkDto Source, EntryDetails Entry, EntryResultDetails Result,
            string HorseName, string? JockeyName, string? TrainerName)> entries,
        CollectionPlatformStore store, IDbContextProvider<EventStoreDbContext> dbContextProvider,
        CancellationToken cancellationToken)
    {
        var today = JstTime.Today();
        var realtime = raceDate >= today && raceDate <= today.AddDays(7);
        var lane = realtime ? CollectionLane.Realtime : CollectionLane.Normal;
        var priority = realtime ? (int)CollectionPriority.High : (int)CollectionPriority.Low;
        using var db = dbContextProvider.CreateContext();
        var ownerAliases = await db.OwnerAliasMappings.AsNoTracking()
            .ToDictionaryAsync(x => x.NormalizedAlias, x => x.OwnerId, cancellationToken).ConfigureAwait(false);
        var candidates = entries.SelectMany(item => new[]
            {
                new SubjectJob(CollectionResourceType.Horse, item.Entry.HorseId, item.HorseName,
                    item.Source.HorseSourceIdentity),
                new SubjectJob(CollectionResourceType.Jockey, item.Entry.JockeyId, item.JockeyName,
                    item.Source.JockeyProfileUrl),
                new SubjectJob(CollectionResourceType.Trainer, item.Entry.TrainerId, item.TrainerName,
                    item.Source.TrainerProfileUrl),
                new SubjectJob(CollectionResourceType.Owner,
                    OwnerIdentityContract.ResolveId(item.Source.OwnerName, ownerAliases),
                    item.Source.OwnerName, null),
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
            .DistinctBy(item => (item.Type, item.Id))
            .OrderBy(item => item.Type).ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0) return [];

        var race = await db.Set<RacePredictionContextReadModel>().AsNoTracking()
            .SingleAsync(item => item.RaceId == raceId, cancellationToken).ConfigureAwait(false);
        var horseIds = candidates.Where(x => x.Type == CollectionResourceType.Horse).Select(x => x.Id!).ToArray();
        var jockeyIds = candidates.Where(x => x.Type == CollectionResourceType.Jockey).Select(x => x.Id!).ToArray();
        var trainerIds = candidates.Where(x => x.Type == CollectionResourceType.Trainer).Select(x => x.Id!).ToArray();
        var horses = await db.Set<HorseReadModel>().AsNoTracking().Where(x => horseIds.Contains(x.HorseId))
            .ToDictionaryAsync(x => x.HorseId, cancellationToken).ConfigureAwait(false);
        var jockeys = await db.Set<JockeyReadModel>().AsNoTracking().Where(x => jockeyIds.Contains(x.JockeyId))
            .ToDictionaryAsync(x => x.JockeyId, cancellationToken).ConfigureAwait(false);
        var trainers = await db.Set<TrainerReadModel>().AsNoTracking().Where(x => trainerIds.Contains(x.TrainerId))
            .ToDictionaryAsync(x => x.TrainerId, cancellationToken).ConfigureAwait(false);
        var resolvedHorseJobs = new Dictionary<string, string>(StringComparer.Ordinal);
        var horseIdentities = await CollectionIdentityResolver.LoadHorsesAsync(db, cancellationToken);
        foreach (var subject in candidates.Where(x => x.Type == CollectionResourceType.Horse))
            resolvedHorseJobs[subject.Id!] = CollectionIdentityResolver.ResolveHorse(horseIdentities, subject.Name!, subject.SourceIdentity, null);
        var rejected = new List<CollectionRequestBatchOutcome>();
        var repairIssues = new List<SubjectIdentificationRepairIssue>();
        var ready = candidates.Where(subject =>
        {
            var referenced = subject.Type switch
            {
                CollectionResourceType.Horse => race.Entries.Any(x => x.HorseId == subject.Id),
                CollectionResourceType.Jockey => race.Entries.Any(x => x.JockeyId == subject.Id),
                CollectionResourceType.Trainer => race.Entries.Any(x => x.TrainerId == subject.Id),
                CollectionResourceType.Owner => true,
                _ => false,
            };
            var projectedName = subject.Type switch
            {
                CollectionResourceType.Horse when horses.TryGetValue(subject.Id!, out var horse) => horse.RegisteredName,
                CollectionResourceType.Jockey when jockeys.TryGetValue(subject.Id!, out var jockey) => jockey.DisplayName,
                CollectionResourceType.Trainer when trainers.TryGetValue(subject.Id!, out var trainer) => trainer.DisplayName,
                CollectionResourceType.Owner => subject.Name,
                _ => null,
            };
            var valid = referenced && projectedName is not null
                && string.Equals(JraSubjectNameNormalizer.NormalizeIdentityName(subject.Type.ToString(), projectedName),
                    JraSubjectNameNormalizer.NormalizeIdentityName(subject.Type.ToString(), subject.Name!),
                    StringComparison.Ordinal)
                && string.Equals(subject.Id, subject.Type == CollectionResourceType.Horse
                    ? resolvedHorseJobs[subject.Id!] : ExpectedSubjectJobId(subject), StringComparison.Ordinal);
            if (valid || subject.Type == CollectionResourceType.Owner) return true;
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
                DefinitionId = SubjectCollectionDefinitions.For(subject.Type).Definition.Value,
                RequestedByRaceId = raceId,
                SourceIdentity = subject.SourceIdentity,
                SourceUrl = subject.Type is CollectionResourceType.Jockey or CollectionResourceType.Trainer ? subject.SourceIdentity : null,
                ReasonCode = "SubjectIdentityRepairRequired",
                ReasonMessage = "主体投影、名称、決定論的ID、またはRaceEntry参照が一致しません。",
                EvidenceFingerprint = fingerprint,
                Status = "Open",
                CreatedAt = JstTime.Now(),
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

        var revisions = SubjectCollectionDefinitions.All.ToDictionary(x => x.Definition.Value, x => x.CurrentRevision);
        foreach (var definitionId in revisions.Keys.ToArray())
        {
            try { revisions[definitionId] = await store.GetCurrentRevisionAsync(new(definitionId), cancellationToken); }
            catch (InvalidOperationException) { /* Missing definitions remain structured per-item failures below. */ }
        }
        var items = ready.Select(subject =>
        {
            var definition = SubjectCollectionDefinitions.For(subject.Type).Definition.Value;
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = subject.Name!,
                ["requestedByRaceId"] = raceId,
                ["weekendPriorityUntil"] = raceDate.ToString("yyyy-MM-dd"),
                ["discoveredFromType"] = CollectionResourceType.Race.ToString(),
                ["discoveredFromProvider"] = "JRA",
                ["discoveredFromId"] = raceId,
            };
            Uri? explicitUrl = null;
            if (subject.Type == CollectionResourceType.Horse
                && JraSourceIdentity.TryNormalizeHorse(subject.SourceIdentity, out _))
            {
                explicitUrl = JraSourceIdentity.NormalizeHorseUrl(subject.SourceIdentity);
                attributes["sourceIdentity"] = explicitUrl!.AbsoluteUri;
                attributes["sourceUrl"] = explicitUrl.AbsoluteUri;
            }
            else if (subject.Type is CollectionResourceType.Jockey or CollectionResourceType.Trainer
                     && IsAllowedJraProfileUrl(subject.SourceIdentity, subject.Type, out var profileUrl))
            {
                explicitUrl = profileUrl!;
                attributes["sourceUrl"] = explicitUrl.AbsoluteUri;
            }
            return new CollectionRequestBatchItem($"{subject.Type}:{subject.Id}",
                new(subject.Type, "JRA", subject.Id!), new(definition), revisions[definition],
                CollectionReason.Discovery, lane, priority, explicitUrl, raceDate, attributes);
        }).ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            items.Select(item => $"{item.ItemKey}:{item.RequestedRevision}"))))).ToLowerInvariant()[..24];
        var accepted = items.Length == 0 ? [] : await store.RequestManyAsync(
            $"race-subjects:{raceId}:{fingerprint}", items, JstTime.Now(), cancellationToken)
            .ConfigureAwait(false);
        return [.. accepted, .. rejected];
    }

    internal sealed record SubjectJob(CollectionResourceType Type, string? Id, string? Name, string? SourceIdentity);

    internal static string? ExpectedSubjectJobId(SubjectJob subject)
    {
        var canonical = JraSubjectNameNormalizer.CanonicalizeDisplayName(
            subject.Type.ToString(), subject.Name!);
        return subject.Type switch
        {
            CollectionResourceType.Jockey => DeterministicIdGenerator.BuildEntityId("jockey",
                DeterministicIdGenerator.NormalizeKey(canonical)),
            CollectionResourceType.Trainer => DeterministicIdGenerator.BuildEntityId("trainer",
                DeterministicIdGenerator.NormalizeKey(canonical)),
            CollectionResourceType.Owner => subject.Id,
            _ => null,
        };
    }

    internal static bool IsAllowedJraProfileUrl(string? value, CollectionResourceType type, out Uri? uri)
    {
        uri = Uri.TryCreate(value, UriKind.Absolute, out var parsed) ? parsed : null;
        var path = type == CollectionResourceType.Jockey ? "/JRADB/accessK.html" : "/JRADB/accessC.html";
        return uri is not null && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("www.jra.go.jp", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(uri.Query.TrimStart('?'));
    }

    internal static void ValidateCollectedRaceResultBulk(BulkRaceResultData data,
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
            || data.Entries.Where(item => item.HorseNumber.HasValue).Select(item => item.HorseNumber).Distinct().Count()
                != data.Entries.Count(item => item.HorseNumber.HasValue))
            throw new ArgumentException("Incoming race entries must be unique.");
        if (data.EntryResults.Select(item => item.EntryId).Distinct(StringComparer.Ordinal).Count()
            != data.EntryResults.Count)
            throw new ArgumentException("Entry result IDs must be unique.");

        var knownEntryIds = (existing?.Entries ?? []).Select(item => item.EntryId)
            .Concat(data.Entries.Select(item => item.EntryId)).ToHashSet(StringComparer.Ordinal);
        if (data.EntryResults.Any(item => !knownEntryIds.Contains(item.EntryId)))
            throw new ArgumentException("Every entry result must reference a registered or incoming entry.");

        var effectiveStatus = existing?.Status ?? Domain.Races.RaceStatus.Draft;
        if (effectiveStatus == Domain.Races.RaceStatus.Draft && data.EntryCount is > 0)
            effectiveStatus = Domain.Races.RaceStatus.CardPublished;
        if (!string.IsNullOrWhiteSpace(data.WinningHorseName) && effectiveStatus < Domain.Races.RaceStatus.ResultDeclared)
            effectiveStatus = Domain.Races.RaceStatus.ResultDeclared;
        if (data.Entries.Count > 0 && effectiveStatus == Domain.Races.RaceStatus.Draft)
            throw new ArgumentException("Entries require a published race card.");
        if (data.EntryResults.Count > 0 && effectiveStatus < Domain.Races.RaceStatus.ResultDeclared)
            throw new ArgumentException("Entry results require a declared race result.");
        if (data.Payouts is not null && effectiveStatus < Domain.Races.RaceStatus.ResultDeclared)
            throw new ArgumentException("Payouts require a declared race result.");
    }

    internal static IReadOnlyList<PayoutEntry> ToPayoutEntries(IReadOnlyList<PayoutEntryDto>? values)
        => values?.Select(item => new PayoutEntry(item.Combination, item.Amount)).ToArray() ?? [];

    internal static void MarkAcceptedOutcomesFailed(List<DeclareRaceResultBulkItemOutcome> outcomes,
        string errorCode, string message)
    {
        for (var index = 0; index < outcomes.Count; index++)
            if (outcomes[index].Status == "Accepted")
                outcomes[index] = outcomes[index] with { Status = "Failed", ErrorCode = errorCode, Message = message };
    }

    internal static async Task EnsureRelatedSubjectsBulkAsync(
        IEnumerable<(RaceResultEntryBulkDto Source, EntryDetails Entry, EntryResultDetails Result,
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
