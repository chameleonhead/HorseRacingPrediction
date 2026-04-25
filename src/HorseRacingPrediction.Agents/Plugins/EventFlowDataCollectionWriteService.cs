using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EventFlow;
using EventFlow.Commands;
using EventFlow.Queries;
using EventFlow.ReadStores.InMemory;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Jockeys;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Domain.Trainers;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// EventFlow の <see cref="ICommandBus"/> と <see cref="IQueryProcessor"/> を使って
/// <see cref="IDataCollectionWriteService"/> を実装するクラス。
/// <para>
/// レース・馬・騎手・調教師の Upsert ロジック（存在確認 → 作成 or 更新）および
/// 決定論的 ID 生成はこのクラスに集約されている。
/// </para>
/// </summary>
public sealed class EventFlowDataCollectionWriteService : IDataCollectionWriteService
{
    private readonly ICommandBus _commandBus;
    private readonly IQueryProcessor _queryProcessor;

    public EventFlowDataCollectionWriteService(ICommandBus commandBus, IQueryProcessor queryProcessor)
    {
        _commandBus = commandBus;
        _queryProcessor = queryProcessor;
    }

    // ------------------------------------------------------------------ //
    // IDataCollectionWriteService
    // ------------------------------------------------------------------ //

    public async Task<string> UpsertRaceAsync(
        string raceDate,
        string racecourseCode,
        int raceNumber,
        string raceName,
        int? entryCount,
        string? gradeCode,
        string? surfaceCode,
        int? distanceMeters,
        string? directionCode,
        CancellationToken cancellationToken = default)
    {
        var parsedRaceDate = DateOnly.Parse(raceDate, CultureInfo.InvariantCulture);
        var raceId = DeterministicIdGenerator.BuildRaceId(parsedRaceDate, racecourseCode, raceNumber);

        var existing = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId.Value),
            cancellationToken);

        if (existing is null || string.IsNullOrEmpty(existing.RaceId))
        {
            var createCommand = new CreateRaceCommand(
                raceId,
                parsedRaceDate,
                racecourseCode,
                raceNumber,
                raceName,
                gradeCode: gradeCode,
                surfaceCode: surfaceCode,
                distanceMeters: distanceMeters,
                directionCode: directionCode);

            await PublishOrThrowAsync(createCommand, $"レース作成に失敗しました: {raceId.Value}", cancellationToken);
        }
        else
        {
            var correctCommand = new CorrectRaceDataCommand(
                raceId,
                raceName: raceName,
                racecourseCode: racecourseCode,
                raceNumber: raceNumber,
                gradeCode: gradeCode,
                surfaceCode: surfaceCode,
                distanceMeters: distanceMeters,
                directionCode: directionCode,
                reason: "Collected by data collection agent");

            await PublishOrThrowAsync(correctCommand, $"レース更新に失敗しました: {raceId.Value}", cancellationToken);
        }

        if (entryCount is > 0 && (existing is null || existing.Status == RaceStatus.Draft))
        {
            var publishCardCommand = new PublishRaceCardCommand(raceId, entryCount.Value);
            await PublishOrThrowAsync(
                publishCardCommand,
                $"出馬表公開に失敗しました: {raceId.Value}",
                cancellationToken);
        }

        return raceId.Value;
    }

    public async Task<string> UpsertHorseAsync(
        string registeredName,
        string? normalizedName,
        string? sexCode,
        string? birthDate,
        CancellationToken cancellationToken = default)
    {
        var normalized = DeterministicIdGenerator.NormalizeDisplayName(normalizedName ?? registeredName);
        var horseId = new HorseId(DeterministicIdGenerator.BuildEntityId("horse", normalized));
        var existing = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<HorseReadModel>(horseId.Value),
            cancellationToken);
        var parsedBirthDate = TryParseDateOnly(birthDate);

        if (existing is null || string.IsNullOrEmpty(existing.HorseId))
        {
            var registerCommand = new RegisterHorseCommand(horseId, registeredName, normalized, sexCode, parsedBirthDate);
            await PublishOrThrowAsync(registerCommand, $"馬登録に失敗しました: {registeredName}", cancellationToken);
        }
        else
        {
            var updateCommand = new UpdateHorseProfileCommand(horseId, registeredName, normalized, sexCode, parsedBirthDate);
            await PublishOrThrowAsync(updateCommand, $"馬更新に失敗しました: {registeredName}", cancellationToken);
        }

        return horseId.Value;
    }

    public async Task<string> UpsertJockeyAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = DeterministicIdGenerator.NormalizeDisplayName(normalizedName ?? displayName);
        var jockeyId = new JockeyId(DeterministicIdGenerator.BuildEntityId("jockey", normalized));
        var existing = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<JockeyReadModel>(jockeyId.Value),
            cancellationToken);

        if (existing is null || string.IsNullOrEmpty(existing.JockeyId))
        {
            var registerCommand = new RegisterJockeyCommand(jockeyId, displayName, normalized, affiliationCode);
            await PublishOrThrowAsync(registerCommand, $"騎手登録に失敗しました: {displayName}", cancellationToken);
        }
        else
        {
            var updateCommand = new UpdateJockeyProfileCommand(jockeyId, displayName, normalized, affiliationCode);
            await PublishOrThrowAsync(updateCommand, $"騎手更新に失敗しました: {displayName}", cancellationToken);
        }

        return jockeyId.Value;
    }

    public async Task<string> UpsertTrainerAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = DeterministicIdGenerator.NormalizeDisplayName(normalizedName ?? displayName);
        var trainerId = new TrainerId(DeterministicIdGenerator.BuildEntityId("trainer", normalized));
        var existing = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<TrainerReadModel>(trainerId.Value),
            cancellationToken);

        if (existing is null || string.IsNullOrEmpty(existing.TrainerId))
        {
            var registerCommand = new RegisterTrainerCommand(trainerId, displayName, normalized, affiliationCode);
            await PublishOrThrowAsync(registerCommand, $"調教師登録に失敗しました: {displayName}", cancellationToken);
        }
        else
        {
            var updateCommand = new UpdateTrainerProfileCommand(trainerId, displayName, normalized, affiliationCode);
            await PublishOrThrowAsync(updateCommand, $"調教師更新に失敗しました: {displayName}", cancellationToken);
        }

        return trainerId.Value;
    }

    public async Task<string> UpsertRaceEntryAsync(
        string raceId,
        int horseNumber,
        string horseName,
        string? jockeyName,
        string? trainerName,
        int? gateNumber,
        decimal? assignedWeight,
        string? sexCode,
        int? age,
        decimal? declaredWeight,
        decimal? declaredWeightDiff,
        CancellationToken cancellationToken = default)
    {
        var race = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId),
            cancellationToken);

        if (race is null || string.IsNullOrEmpty(race.RaceId))
            throw new InvalidOperationException($"レースが存在しません: {raceId}");

        if (race.Entries.Any(entry => entry.HorseNumber == horseNumber))
            return $"レース {raceId} の馬番 {horseNumber} は既に登録済みです。";

        var horseId = await UpsertHorseAsync(horseName, normalizedName: null, sexCode: sexCode, birthDate: null, cancellationToken: cancellationToken);
        var jockeyId = string.IsNullOrWhiteSpace(jockeyName)
            ? null
            : await UpsertJockeyAsync(jockeyName, null, null, cancellationToken);
        var trainerId = string.IsNullOrWhiteSpace(trainerName)
            ? null
            : await UpsertTrainerAsync(trainerName, null, null, cancellationToken);

        var command = new RegisterEntryCommand(
            new RaceId(raceId),
            DeterministicIdGenerator.BuildRaceEntryId(raceId, horseNumber),
            horseId,
            horseNumber,
            jockeyId,
            trainerId,
            gateNumber,
            assignedWeight,
            sexCode,
            age,
            declaredWeight,
            declaredWeightDiff);

        await PublishOrThrowAsync(command, $"出走登録に失敗しました: raceId={raceId}, horseNumber={horseNumber}", cancellationToken);
        return $"レース {raceId} に馬番 {horseNumber} の出走登録を行いました。";
    }

    public async Task<string> DeclareRaceResultAsync(
        string raceId,
        string winningHorseName,
        string? declaredAt,
        string? winningHorseId,
        CancellationToken cancellationToken = default)
    {
        var race = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId),
            cancellationToken);

        if (race is null || string.IsNullOrEmpty(race.RaceId))
            throw new InvalidOperationException($"レースが存在しません: {raceId}");

        if (race.Status == RaceStatus.Draft)
        {
            var publishCardCommand = new PublishRaceCardCommand(new RaceId(raceId), race.Entries.Count > 0 ? race.Entries.Count : 1);
            await PublishOrThrowAsync(publishCardCommand, $"出馬表公開に失敗しました: {raceId}", cancellationToken);
        }

        var parsedAt = declaredAt is not null
            ? DateTimeOffset.Parse(declaredAt, CultureInfo.InvariantCulture)
            : DateTimeOffset.UtcNow;

        var command = new DeclareRaceResultCommand(new RaceId(raceId), winningHorseName, parsedAt, winningHorseId);
        await PublishOrThrowAsync(command, $"レース結果宣言に失敗しました: {raceId}", cancellationToken);
        return $"レース {raceId} の確定結果（勝ち馬: {winningHorseName}）を記録しました。";
    }

    public async Task<string> DeclareRaceEntryResultAsync(
        string raceId,
        int horseNumber,
        int? finishPosition,
        string? officialTime,
        string? marginText,
        string? lastThreeFurlongTime,
        string? abnormalResultCode,
        decimal? prizeMoney,
        CancellationToken cancellationToken = default)
    {
        var entryId = DeterministicIdGenerator.BuildRaceEntryId(raceId, horseNumber);
        var command = new DeclareEntryResultCommand(
            new RaceId(raceId),
            entryId,
            finishPosition,
            officialTime,
            marginText,
            lastThreeFurlongTime,
            abnormalResultCode,
            prizeMoney);
        await PublishOrThrowAsync(command, $"エントリ結果記録に失敗しました: raceId={raceId}, horseNumber={horseNumber}", cancellationToken);
        return $"レース {raceId} の馬番 {horseNumber} の成績を記録しました。";
    }

    public async Task<string> DeclareRacePayoutsAsync(
        string raceId,
        string? winPayoutsJson,
        string? placePayoutsJson,
        string? quinellaPayoutsJson,
        string? exactaPayoutsJson,
        string? trifectaPayoutsJson,
        CancellationToken cancellationToken = default)
    {
        var command = new DeclarePayoutResultCommand(
            new RaceId(raceId),
            DateTimeOffset.UtcNow,
            ParsePayouts(winPayoutsJson),
            ParsePayouts(placePayoutsJson),
            ParsePayouts(quinellaPayoutsJson),
            ParsePayouts(exactaPayoutsJson),
            ParsePayouts(trifectaPayoutsJson));
        await PublishOrThrowAsync(command, $"払い戻し記録に失敗しました: {raceId}", cancellationToken);
        return $"レース {raceId} の払い戻しを記録しました。";
    }

    // ------------------------------------------------------------------ //
    // private helpers — EventFlow publishing
    // ------------------------------------------------------------------ //

    private async Task PublishOrThrowAsync<TAggregate, TIdentity>(
        Command<TAggregate, TIdentity> command,
        string errorMessage,
        CancellationToken cancellationToken)
        where TAggregate : EventFlow.Aggregates.IAggregateRoot<TIdentity>
        where TIdentity : EventFlow.Core.IIdentity
    {
        var result = await _commandBus.PublishAsync(command, cancellationToken);
        if (!result.IsSuccess)
            throw new InvalidOperationException(errorMessage);
    }

    // ------------------------------------------------------------------ //
    // private helpers — payout parsing
    // ------------------------------------------------------------------ //

    private static IReadOnlyList<PayoutEntry>? ParsePayouts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var dtos = JsonSerializer.Deserialize<List<PayoutDto>>(json);
            return dtos?.Select(d => new PayoutEntry(d.Combination ?? string.Empty, d.Amount)).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class PayoutDto
    {
        [JsonPropertyName("combination")]
        public string? Combination { get; init; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; init; }
    }

    // ------------------------------------------------------------------ //
    // private helpers — date parsing
    // ------------------------------------------------------------------ //

    private static DateOnly? TryParseDateOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
