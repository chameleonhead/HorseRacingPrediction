using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// データ収集エージェントがドメインモデルを更新するための書き込み系ツール。
/// 実際の書き込みロジックは <see cref="IDataCollectionWriteService"/> に委譲する。
/// <see cref="GetAITools"/> で <see cref="AITool"/> 一覧を取得し、
/// <see cref="Microsoft.Agents.AI.ChatClientAgent"/> に渡すことで利用可能になる。
/// </summary>
public sealed class DataCollectionWriteTools
{
    private readonly IDataCollectionWriteService _service;

    public DataCollectionWriteTools(IDataCollectionWriteService service)
    {
        _service = service;
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
        return await _service.UpsertRaceAsync(
            raceDate, racecourseCode, raceNumber, raceName,
            entryCount, gradeCode, surfaceCode, distanceMeters, directionCode,
            cancellationToken);
    }

    [Description("競走馬を作成または更新し、馬 ID を返します。")]
    public async Task<string> UpsertHorse(
        [Description("馬名")] string registeredName,
        [Description("正規化名。省略時は馬名をそのまま使います")] string? normalizedName = null,
        [Description("性別コード。牡, 牝, セ など")] string? sexCode = null,
        [Description("生年月日。YYYY-MM-DD 形式。分かる場合のみ")] string? birthDate = null,
        CancellationToken cancellationToken = default)
    {
        return await _service.UpsertHorseAsync(registeredName, normalizedName, sexCode, birthDate, cancellationToken);
    }

    [Description("騎手を作成または更新し、騎手 ID を返します。")]
    public async Task<string> UpsertJockey(
        [Description("騎手名")] string displayName,
        [Description("正規化名。省略時は騎手名をそのまま使います")] string? normalizedName = null,
        [Description("所属コード。JRA, 地方, 美浦, 栗東 など")] string? affiliationCode = null,
        CancellationToken cancellationToken = default)
    {
        return await _service.UpsertJockeyAsync(displayName, normalizedName, affiliationCode, cancellationToken);
    }

    [Description("調教師を作成または更新し、調教師 ID を返します。")]
    public async Task<string> UpsertTrainer(
        [Description("調教師名")] string displayName,
        [Description("正規化名。省略時は調教師名をそのまま使います")] string? normalizedName = null,
        [Description("所属コード。美浦, 栗東, 地方 など")] string? affiliationCode = null,
        CancellationToken cancellationToken = default)
    {
        return await _service.UpsertTrainerAsync(displayName, normalizedName, affiliationCode, cancellationToken);
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
        return await _service.UpsertRaceEntryAsync(
            raceId, horseNumber, horseName, jockeyName, trainerName,
            gateNumber, assignedWeight, sexCode, age,
            declaredWeight, declaredWeightDiff,
            cancellationToken);
    }

    [Description("レース全体の確定結果（勝ち馬）を宣言します。成績収集後に呼び出してください。")]
    public async Task<string> DeclareRaceResult(
        [Description("レース ID")] string raceId,
        [Description("勝ち馬の馬名")] string winningHorseName,
        [Description("結果確定日時。省略時は現在時刻を使います")] string? declaredAt = null,
        [Description("勝ち馬の馬 ID。省略可")] string? winningHorseId = null,
        CancellationToken cancellationToken = default)
    {
        return await _service.DeclareRaceResultAsync(raceId, winningHorseName, declaredAt, winningHorseId, cancellationToken);
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
        return await _service.DeclareRaceEntryResultAsync(
            raceId, horseNumber, finishPosition, officialTime, marginText,
            lastThreeFurlongTime, abnormalResultCode, prizeMoney,
            cancellationToken);
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
        return await _service.DeclareRacePayoutsAsync(
            raceId, winPayoutsJson, placePayoutsJson, quinellaPayoutsJson,
            exactaPayoutsJson, trifectaPayoutsJson,
            cancellationToken);
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
}
