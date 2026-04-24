using System.ComponentModel;
using System.Globalization;
using System.Text;
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
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// データ収集エージェントが EventFlow 経由でドメインモデルを更新するための書き込み系ツール。
/// </summary>
public sealed class DataCollectionWriteTools
{
    private readonly ICommandBus _commandBus;
    private readonly IQueryProcessor _queryProcessor;

    public DataCollectionWriteTools(ICommandBus commandBus, IQueryProcessor queryProcessor)
    {
        _commandBus = commandBus;
        _queryProcessor = queryProcessor;
    }

    [Description("レース情報を作成または更新し、レース ID を返します。出馬表を登録する前に呼び出してください。")]
    public async Task<string> UpsertRace(
        [Description("開催日。YYYY-MM-DD 形式")] string raceDate,
        [Description("競馬場コードまたは競馬場名")] string racecourseCode,
        [Description("レース番号")] int raceNumber,
        [Description("レース名")] string raceName,
        [Description("出走頭数。分かる場合のみ")] int? entryCount = null,
        [Description("グレードコード。G1, G2, G3 など")] string? gradeCode = null,
        [Description("馬場種別コード。芝, ダート など")] string? surfaceCode = null,
        [Description("距離(m)")] int? distanceMeters = null,
        [Description("回り方向。右, 左, 直線 など")] string? directionCode = null,
        CancellationToken cancellationToken = default)
    {
        var parsedRaceDate = DateOnly.Parse(raceDate, CultureInfo.InvariantCulture);
        var raceId = BuildRaceId(parsedRaceDate, racecourseCode, raceNumber);

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

    [Description("競走馬を作成または更新し、馬 ID を返します。")]
    public async Task<string> UpsertHorse(
        [Description("馬名")] string registeredName,
        [Description("正規化名。省略時は馬名をそのまま使います")] string? normalizedName = null,
        [Description("性別コード。牡, 牝, セ など")] string? sexCode = null,
        [Description("生年月日。YYYY-MM-DD 形式。分かる場合のみ")] string? birthDate = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeDisplayName(normalizedName ?? registeredName);
        var horseId = new HorseId(BuildEntityId("horse", normalized));
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

    [Description("騎手を作成または更新し、騎手 ID を返します。")]
    public async Task<string> UpsertJockey(
        [Description("騎手名")] string displayName,
        [Description("正規化名。省略時は騎手名をそのまま使います")] string? normalizedName = null,
        [Description("所属コード。JRA, 地方, 美浦, 栗東 など")] string? affiliationCode = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeDisplayName(normalizedName ?? displayName);
        var jockeyId = new JockeyId(BuildEntityId("jockey", normalized));
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

    [Description("調教師を作成または更新し、調教師 ID を返します。")]
    public async Task<string> UpsertTrainer(
        [Description("調教師名")] string displayName,
        [Description("正規化名。省略時は調教師名をそのまま使います")] string? normalizedName = null,
        [Description("所属コード。美浦, 栗東, 地方 など")] string? affiliationCode = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeDisplayName(normalizedName ?? displayName);
        var trainerId = new TrainerId(BuildEntityId("trainer", normalized));
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

    [Description("レースの出走エントリーを作成します。事前に UpsertRace でレースを作成してください。")]
    public async Task<string> UpsertRaceEntry(
        [Description("レース ID")] string raceId,
        [Description("馬番")] int horseNumber,
        [Description("馬名")] string horseName,
        [Description("騎手名")] string? jockeyName = null,
        [Description("調教師名")] string? trainerName = null,
        [Description("枠番")] int? gateNumber = null,
        [Description("斤量")] decimal? assignedWeight = null,
        [Description("性別コード。牡, 牝, セ など")] string? sexCode = null,
        [Description("馬齢")] int? age = null,
        [Description("馬体重")] decimal? declaredWeight = null,
        [Description("馬体重増減")] decimal? declaredWeightDiff = null,
        CancellationToken cancellationToken = default)
    {
        var race = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId),
            cancellationToken);

        if (race is null || string.IsNullOrEmpty(race.RaceId))
            throw new InvalidOperationException($"レースが存在しません: {raceId}");

        if (race.Entries.Any(entry => entry.HorseNumber == horseNumber))
            return $"レース {raceId} の馬番 {horseNumber} は既に登録済みです。";

        var horseId = await UpsertHorse(horseName, sexCode: sexCode, cancellationToken: cancellationToken);
        var jockeyId = string.IsNullOrWhiteSpace(jockeyName)
            ? null
            : await UpsertJockey(jockeyName, cancellationToken: cancellationToken);
        var trainerId = string.IsNullOrWhiteSpace(trainerName)
            ? null
            : await UpsertTrainer(trainerName, cancellationToken: cancellationToken);

        var command = new RegisterEntryCommand(
            new RaceId(raceId),
            BuildRaceEntryId(raceId, horseNumber),
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

    [Description("レース全体の確定結果（勝ち馬）を宣言します。成績収集後に呼び出してください。")]
    public async Task<string> DeclareRaceResult(
        [Description("レース ID")] string raceId,
        [Description("勝ち馬の馬名")] string winningHorseName,
        [Description("結果確定日時。省略時は現在時刻を使います")] string? declaredAt = null,
        [Description("勝ち馬の馬 ID。省略可")] string? winningHorseId = null,
        CancellationToken cancellationToken = default)
    {
        var race = await _queryProcessor.ProcessAsync(
            new ReadModelByIdQuery<RacePredictionContextReadModel>(raceId),
            cancellationToken);

        if (race is null || string.IsNullOrEmpty(race.RaceId))
            throw new InvalidOperationException($"レースが存在しません: {raceId}");

        // Draft 状態のときは出馬表公開まで進める
        if (race.Status == RaceStatus.Draft)
        {
            var publishCardCommand = new PublishRaceCardCommand(new RaceId(raceId), race.Entries.Count > 0 ? race.Entries.Count : 1);
            await PublishOrThrowAsync(publishCardCommand, $"出馬表公開に失敗しました: {raceId}", cancellationToken);
        }

        var parsedAt = declaredAt is not null
            ? DateTimeOffset.Parse(declaredAt, System.Globalization.CultureInfo.InvariantCulture)
            : DateTimeOffset.UtcNow;

        var command = new DeclareRaceResultCommand(new RaceId(raceId), winningHorseName, parsedAt, winningHorseId);
        await PublishOrThrowAsync(command, $"レース結果宣言に失敗しました: {raceId}", cancellationToken);
        return $"レース {raceId} の確定結果（勝ち馬: {winningHorseName}）を記録しました。";
    }

    [Description("出走馬1頭分の着順・タイムなどの成績を記録します。DeclareRaceResult の後に呼び出してください。")]
    public async Task<string> DeclareRaceEntryResult(
        [Description("レース ID")] string raceId,
        [Description("馬番")] int horseNumber,
        [Description("着順。取消・除外時は null")] int? finishPosition = null,
        [Description("タイム（例: 1:59.8）")] string? officialTime = null,
        [Description("着差テキスト（例: ハナ, 1/2, 1）")] string? marginText = null,
        [Description("後3F タイム（例: 34.2）")] string? lastThreeFurlongTime = null,
        [Description("異常コード（取消, 除外, 中止, 降着, 失格 など）")] string? abnormalResultCode = null,
        [Description("賞金（万円）")] decimal? prizeMoney = null,
        CancellationToken cancellationToken = default)
    {
        var entryId = BuildRaceEntryId(raceId, horseNumber);
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

    [Description("払い戻しデータを記録します。DeclareRaceResult の後に呼び出してください。")]
    public async Task<string> DeclareRacePayouts(
        [Description("レース ID")] string raceId,
        [Description("単勝払い戻し。馬番と金額のペアのリスト（JSON 配列: [{\"combination\":\"3\",\"amount\":430}]）")] string? winPayoutsJson = null,
        [Description("複勝払い戻し。馬番と金額のペアのリスト")] string? placePayoutsJson = null,
        [Description("馬連払い戻し。組み合わせと金額のペアのリスト")] string? quinellaPayoutsJson = null,
        [Description("馬単払い戻し。組み合わせと金額のペアのリスト")] string? exactaPayoutsJson = null,
        [Description("三連単払い戻し。組み合わせと金額のペアのリスト")] string? trifectaPayoutsJson = null,
        CancellationToken cancellationToken = default)
    {
        static IReadOnlyList<PayoutEntry>? ParsePayouts(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var dtos = System.Text.Json.JsonSerializer.Deserialize<List<PayoutDto>>(json);
                return dtos?.Select(d => new PayoutEntry(d.Combination ?? string.Empty, d.Amount)).ToList();
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

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

    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(UpsertRace),
        AIFunctionFactory.Create(UpsertHorse),
        AIFunctionFactory.Create(UpsertJockey),
        AIFunctionFactory.Create(UpsertTrainer),
        AIFunctionFactory.Create(UpsertRaceEntry),
        AIFunctionFactory.Create(DeclareRaceResult),
        AIFunctionFactory.Create(DeclareRaceEntryResult),
        AIFunctionFactory.Create(DeclareRacePayouts),
    ];

    private sealed class PayoutDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("combination")]
        public string? Combination { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("amount")]
        public decimal Amount { get; init; }
    }

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

    private static DateOnly? TryParseDateOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static readonly Guid HorseNamespaceId = new("1c86504c-11bb-4e95-b997-94d64f0569f3");
    private static readonly Guid JockeyNamespaceId = new("ec7d5b11-f383-4860-88b7-37ef25e4cc81");
    private static readonly Guid TrainerNamespaceId = new("36d7318f-bf48-488f-b0d8-0e2c942b36d2");
    private static readonly Guid RaceNamespaceId = new("d54c5101-305d-42aa-a8df-3c52ca96a6ef");

    private static RaceId BuildRaceId(DateOnly raceDate, string racecourseCode, int raceNumber)
    {
        var normalizedRacecourse = NormalizeKey(racecourseCode);
        var guid = CreateDeterministicGuid(RaceNamespaceId, $"{raceDate:yyyy-MM-dd}|{normalizedRacecourse}|{raceNumber:D2}");
        return new RaceId($"race-{guid:D}");
    }

    private static string BuildRaceEntryId(string raceId, int horseNumber) =>
        $"{raceId}-entry-{horseNumber:D2}";

    private static string BuildEntityId(string prefix, string normalizedName)
    {
        var namespaceId = prefix switch
        {
            "horse" => HorseNamespaceId,
            "jockey" => JockeyNamespaceId,
            "trainer" => TrainerNamespaceId,
            _ => throw new InvalidOperationException($"未知の ID prefix です: {prefix}")
        };

        var guid = CreateDeterministicGuid(namespaceId, normalizedName);
        return $"{prefix}-{guid:D}";
    }

    private static string NormalizeDisplayName(string value) => value.Trim();

    private static string NormalizeKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static Guid CreateDeterministicGuid(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var data = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, data, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, data, namespaceBytes.Length, nameBytes.Length);

        var hash = System.Security.Cryptography.SHA1.HashData(data);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        var guidBytes = hash[..16].ToArray();
        SwapByteOrder(guidBytes);
        return new Guid(guidBytes);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        Swap(guid, 0, 3);
        Swap(guid, 1, 2);
        Swap(guid, 4, 5);
        Swap(guid, 6, 7);
    }

    private static void Swap(byte[] array, int left, int right)
    {
        (array[left], array[right]) = (array[right], array[left]);
    }
}