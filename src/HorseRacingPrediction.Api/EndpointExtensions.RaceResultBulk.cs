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
            var horseId = isExistingEntry
                ? existing!.Entries.Single(entry => entry.EntryId == entryId).HorseId
                : DeterministicIdGenerator.BuildEntityId("horse", DeterministicIdGenerator.NormalizeKey(horseName));
            var jockeyId = string.IsNullOrWhiteSpace(item.JockeyName) ? null : DeterministicIdGenerator.BuildEntityId(
                "jockey", DeterministicIdGenerator.NormalizeKey(item.JockeyName));
            var trainerId = string.IsNullOrWhiteSpace(item.TrainerName) ? null : DeterministicIdGenerator.BuildEntityId(
                "trainer", DeterministicIdGenerator.NormalizeKey(item.TrainerName));
            var entry = new EntryDetails(entryId, horseId, item.HorseNumber, jockeyId, trainerId,
                item.GateNumber, item.AssignedWeight, item.SexCode, item.Age, item.BodyWeight,
                item.BodyWeightChange, null, item.OwnerName);
            var result = new EntryResultDetails(entryId, item.FinishPosition, item.OfficialTime,
                item.MarginText, item.LastThreeFurlongTime, item.AbnormalResultCode, item.PrizeMoney,
                item.CornerPositions, item.Popularity, item.OriginalFinishPosition, item.IsDeadHeat,
                item.Average1F, item.AdditionalPrizeMoney);
            accepted.Add((item, entry, result, horseName, item.JockeyName, item.TrainerName));
            outcomes.Add(new("Entry", key, "Accepted"));
        }

        var gradeCode = ResolveCollectedGradeCode(request.GradeCode, request.RaceName, existing?.RaceName);
        var data = new BulkRaceResultData(
            request.RaceDate, request.RacecourseCode, request.RaceNumber, request.RaceName,
            request.EntryCount, gradeCode, request.SurfaceCode, request.DistanceMeters, request.DirectionCode,
            accepted.Where(item => !existingEntryIds.Contains(item.Entry.EntryId)).Select(item => item.Entry).ToArray(),
            accepted.Select(item => item.Result).ToArray(), request.WinningHorseName,
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
            await EnsureRelatedSubjectsBulkAsync(accepted.Where(item => !existingEntryIds.Contains(item.Entry.EntryId)),
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
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            errors.Add($"レース一括登録エラー: {ex.Message}");
            MarkAcceptedOutcomesFailed(outcomes, "RaceBulkCommandFailed", ex.Message);
        }

        return Results.Ok(new Shared.DeclareRaceResultBulkResponse(raceIdValue, errors, outcomes));
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
