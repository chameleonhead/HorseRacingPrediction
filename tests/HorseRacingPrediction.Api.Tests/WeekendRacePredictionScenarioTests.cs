using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Api.Tests;

/// <summary>
/// 日曜日のG1レース「東京優駿（日本ダービー）」を題材にした予想シナリオテスト。
///
/// シナリオの流れ:
///   木曜日 - 出馬表公開: 馬・騎手・調教師を登録し、レースを作成して出走馬一覧を登録。
///             初期観察メモも記録する。
///   金曜日 - 枠順発表: 枠番付きでエントリーを登録し、予想チケットを作成。
///             印・根拠・馬券提案を追加する。
///   土曜日 - 調教最終確認: 天気と馬場状態を観測し、追加メモを記録。予想を確定する。
///   日曜日 - レース当日: 天気・馬場の最終観測後、レース開始〜結果宣言〜払戻宣言〜
///             レースクローズ〜予想評価まで一連の流れを検証する。
/// </summary>
[TestClass]
public class WeekendRacePredictionScenarioTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static WebApplication _app = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        (_app, _client) = await TestApplicationFactory.CreateAsync();
        _client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
    }

    [ClassCleanup]
    public static async Task ClassClean()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    /// <summary>
    /// 木曜日から日曜日までの完全な競馬予想シナリオを検証する。
    /// </summary>
    [TestMethod]
    public async Task FullWeekendScenario_SundayRace_AllStepsSucceed()
    {
        // ───────────────────────────────────────────────────
        // 共通 ID を事前に用意する
        // ───────────────────────────────────────────────────
        var raceId = $"race-{Guid.NewGuid()}";
        var horse1Id = $"horse-{Guid.NewGuid()}"; // 本命馬
        var horse2Id = $"horse-{Guid.NewGuid()}"; // 対抗馬
        var horse3Id = $"horse-{Guid.NewGuid()}"; // 3番手
        var jockey1Id = $"jockey-{Guid.NewGuid()}";
        var jockey2Id = $"jockey-{Guid.NewGuid()}";
        var jockey3Id = $"jockey-{Guid.NewGuid()}";
        var trainerId = $"trainer-{Guid.NewGuid()}";
        var entry1Id = $"entry-{Guid.NewGuid()}";
        var entry2Id = $"entry-{Guid.NewGuid()}";
        var entry3Id = $"entry-{Guid.NewGuid()}";
        var ticketId = $"predictionticket-{Guid.NewGuid()}";
        var memo1Id = $"memo-{Guid.NewGuid()}";
        var memo2Id = $"memo-{Guid.NewGuid()}";
        var memo3Id = $"memo-{Guid.NewGuid()}";

        // ═══════════════════════════════════════════════════
        // 【木曜日】出馬表公開
        // ═══════════════════════════════════════════════════

        // --- 馬・騎手・調教師を登録する ---
        var registerHorse1 = await _client.PostAsJsonAsync("/api/horses",
            new RegisterHorseRequest("サニーブレイズ", "SUNNYBLAZE", "M", new DateOnly(2022, 4, 10), horse1Id),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, registerHorse1.StatusCode, "本命馬の登録に失敗");

        var registerHorse2 = await _client.PostAsJsonAsync("/api/horses",
            new RegisterHorseRequest("スターライトランナー", "STARLIGHTRUNNER", "M", new DateOnly(2022, 3, 20), horse2Id),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, registerHorse2.StatusCode, "対抗馬の登録に失敗");

        var registerHorse3 = await _client.PostAsJsonAsync("/api/horses",
            new RegisterHorseRequest("エメラルドウィンド", "EMERALDWIND", "M", new DateOnly(2022, 5, 1), horse3Id),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, registerHorse3.StatusCode, "3番手馬の登録に失敗");

        var registerJockey1 = await _client.PostAsJsonAsync("/api/jockeys",
            new RegisterJockeyRequest("田中 剛", "TANAKAGO", "JRA", jockey1Id),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, registerJockey1.StatusCode, "騎手1の登録に失敗");

        var registerJockey2 = await _client.PostAsJsonAsync("/api/jockeys",
            new RegisterJockeyRequest("山本 優", "YAMAMOTOYUU", "JRA", jockey2Id),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, registerJockey2.StatusCode, "騎手2の登録に失敗");

        var registerJockey3 = await _client.PostAsJsonAsync("/api/jockeys",
            new RegisterJockeyRequest("鈴木 健", "SUZUKIKEN", "JRA", jockey3Id),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, registerJockey3.StatusCode, "騎手3の登録に失敗");

        var registerTrainer = await _client.PostAsJsonAsync("/api/trainers",
            new RegisterTrainerRequest("佐藤 誠", "SATOMAKOTO", "JRA", trainerId),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, registerTrainer.StatusCode, "調教師の登録に失敗");

        // --- レースを作成する（Draft 状態） ---
        var createRace = await _client.PostAsJsonAsync("/api/races",
            new CreateRaceRequest(new DateOnly(2025, 6, 1), "TOKYO", 11, "東京優駿（日本ダービー）", raceId),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, createRace.StatusCode, "レース作成に失敗");

        // Draft 状態を確認
        var raceAfterCreate = await _client.GetAsync($"/api/races/{raceId}");
        Assert.AreEqual(HttpStatusCode.OK, raceAfterCreate.StatusCode);
        var raceDraft = await raceAfterCreate.Content.ReadFromJsonAsync<RaceResponse>(JsonOptions);
        Assert.IsNotNull(raceDraft);
        Assert.AreEqual(RaceStatus.Draft, raceDraft.Status, "レース作成直後は Draft であるべき");

        // --- 出馬表を公開する（CardPublished 状態に遷移） ---
        // 木曜日: 出馬表公開。まだ枠順は未確定なので GateNumber は null にして登録する。
        var publishCard = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/card/publish",
            new PublishRaceCardRequest(18),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, publishCard.StatusCode, "出馬表公開に失敗");

        // CardPublished 状態を確認
        var raceAfterPublish = await _client.GetAsync($"/api/races/{raceId}");
        var racePublished = await raceAfterPublish.Content.ReadFromJsonAsync<RaceResponse>(JsonOptions);
        Assert.IsNotNull(racePublished);
        Assert.AreEqual(RaceStatus.CardPublished, racePublished.Status, "出馬表公開後は CardPublished であるべき");

        // --- 出走馬を登録する（枠番は未確定のため null） ---
        var entry1Response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(horse1Id, 1, jockey1Id, trainerId, null, 57.0m, "M", 3, 468.0m, 0.0m, entry1Id),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, entry1Response.StatusCode, "エントリー1の登録に失敗");

        var entry2Response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(horse2Id, 2, jockey2Id, trainerId, null, 57.0m, "M", 3, 472.0m, 2.0m, entry2Id),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, entry2Response.StatusCode, "エントリー2の登録に失敗");

        var entry3Response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(horse3Id, 3, jockey3Id, trainerId, null, 57.0m, "M", 3, 460.0m, -4.0m, entry3Id),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, entry3Response.StatusCode, "エントリー3の登録に失敗");

        // --- 初期観察メモを記録する ---
        var memo1Response = await _client.PostAsJsonAsync("/api/memos",
            new CreateMemoRequest(
                AuthorId: "analyst-1",
                MemoType: "InitialObservation",
                Content: "サニーブレイズは先週の調教で動きが良く、状態上向き。前走の皐月賞で2着からの巻き返しに期待。",
                CreatedAt: DateTimeOffset.UtcNow,
                Subjects: new[]
                {
                    new MemoSubjectDto("Horse", horse1Id),
                    new MemoSubjectDto("Race", raceId)
                },
                MemoId: memo1Id),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, memo1Response.StatusCode, "初期観察メモ1の記録に失敗");

        var memo2Response = await _client.PostAsJsonAsync("/api/memos",
            new CreateMemoRequest(
                AuthorId: "analyst-1",
                MemoType: "InitialObservation",
                Content: "スターライトランナーは距離延長がカギ。2000mまでは好走しているが、2400mは初距離。",
                CreatedAt: DateTimeOffset.UtcNow,
                Subjects: new[]
                {
                    new MemoSubjectDto("Horse", horse2Id),
                    new MemoSubjectDto("Race", raceId)
                },
                MemoId: memo2Id),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, memo2Response.StatusCode, "初期観察メモ2の記録に失敗");

        // ═══════════════════════════════════════════════════
        // 【金曜日】枠順発表・予想作成
        // ═══════════════════════════════════════════════════

        // --- 枠番付きエントリーを追加登録する ---
        // 枠順発表後、実際の枠番・ゲート番号を含めた情報を新たに登録する。
        var entry1WithGate = $"entry-{Guid.NewGuid()}";
        var entry2WithGate = $"entry-{Guid.NewGuid()}";
        var entry3WithGate = $"entry-{Guid.NewGuid()}";

        var gateEntry1Response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(horse1Id, 4, jockey1Id, trainerId, 2, 57.0m, "M", 3, 468.0m, 0.0m, entry1WithGate),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, gateEntry1Response.StatusCode, "枠番付きエントリー1の登録に失敗");

        var gateEntry2Response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(horse2Id, 7, jockey2Id, trainerId, 4, 57.0m, "M", 3, 472.0m, 2.0m, entry2WithGate),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, gateEntry2Response.StatusCode, "枠番付きエントリー2の登録に失敗");

        var gateEntry3Response = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries",
            new RegisterEntryRequest(horse3Id, 11, jockey3Id, trainerId, 6, 57.0m, "M", 3, 460.0m, -4.0m, entry3WithGate),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, gateEntry3Response.StatusCode, "枠番付きエントリー3の登録に失敗");

        // --- 予想コンテキスト（RacePredictionContext）を確認する ---
        var contextResponse = await _client.GetAsync($"/api/races/{raceId}/context");
        Assert.AreEqual(HttpStatusCode.OK, contextResponse.StatusCode, "予想コンテキスト取得に失敗");

        // --- 予想チケットを作成する ---
        var createTicket = await _client.PostAsJsonAsync("/api/predictions",
            new CreatePredictionTicketRequest(
                RaceId: raceId,
                PredictorType: "Human",
                PredictorId: "analyst-1",
                ConfidenceScore: 0.82m,
                SummaryComment: "本命サニーブレイズ。内枠利で先行有利の展開が見込まれる。",
                PredictionTicketId: ticketId),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, createTicket.StatusCode, "予想チケット作成に失敗");

        // --- 印を追加する（◎○▲） ---
        var mark1 = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/marks",
            new AddPredictionMarkRequest(entry1WithGate, "◎", 1, 88.0m, "内枠先行で展開が向く。前走惜敗で反動なし。"),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, mark1.StatusCode, "◎印の追加に失敗");

        var mark2 = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/marks",
            new AddPredictionMarkRequest(entry2WithGate, "○", 2, 72.0m, "距離延長は懸念だが末脚は確か。"),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, mark2.StatusCode, "○印の追加に失敗");

        var mark3 = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/marks",
            new AddPredictionMarkRequest(entry3WithGate, "▲", 3, 60.0m, "外枠も機動力でカバーできるか。"),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, mark3.StatusCode, "▲印の追加に失敗");

        // --- 予想根拠を追加する ---
        var rationale1 = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/rationales",
            new AddPredictionRationaleRequest(
                SubjectType: "Horse",
                SubjectId: horse1Id,
                SignalType: "SPEED_INDEX",
                SignalValue: "118",
                ExplanationText: "スピード指数118はクラス上位水準。前走2着から上積みが見込める。"),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, rationale1.StatusCode, "根拠1の追加に失敗");

        var rationale2 = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/rationales",
            new AddPredictionRationaleRequest(
                SubjectType: "Race",
                SubjectId: raceId,
                SignalType: "TRACK_BIAS",
                SignalValue: "INNER_ADVANTAGE",
                ExplanationText: "東京2400mは前半ペース次第で内枠先行が有利な傾向。"),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, rationale2.StatusCode, "根拠2の追加に失敗");

        // --- 馬券提案を追加する ---
        var suggestion1 = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/betting-suggestions",
            new AddBettingSuggestionRequest("WIN", "4", 2000m, null),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, suggestion1.StatusCode, "単勝馬券提案の追加に失敗");

        var suggestion2 = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/betting-suggestions",
            new AddBettingSuggestionRequest("EXACTA", "4-7", 1000m, null),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, suggestion2.StatusCode, "馬連馬券提案の追加に失敗");

        // 金曜時点での予想チケット内容を確認する
        var ticketAfterFriday = await _client.GetAsync($"/api/predictions/{ticketId}");
        Assert.AreEqual(HttpStatusCode.OK, ticketAfterFriday.StatusCode);
        var ticketFriday = await ticketAfterFriday.Content.ReadFromJsonAsync<PredictionTicketResponse>(JsonOptions);
        Assert.IsNotNull(ticketFriday);
        Assert.AreEqual(raceId, ticketFriday.RaceId);
        Assert.AreEqual(3, ticketFriday.Marks.Count, "3頭分の印が登録されているべき");
        Assert.AreEqual(0.82m, ticketFriday.ConfidenceScore);

        // ═══════════════════════════════════════════════════
        // 【土曜日】調教最終確認・天気・馬場観測
        // ═══════════════════════════════════════════════════

        // --- 天気を観測する（土曜朝） ---
        var satWeather = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/weather",
            new RecordWeatherObservationRequest(
                DateTimeOffset.UtcNow.AddDays(-1),
                WeatherCode: "CLOUDY",
                WeatherText: "曇り",
                TemperatureCelsius: 19.5m,
                HumidityPercent: 68.0m,
                WindDirectionCode: "SW",
                WindSpeedMeterPerSecond: 4.1m),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, satWeather.StatusCode, "土曜天気観測に失敗");

        // --- 馬場状態を観測する（土曜朝） ---
        var satTrack = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/track-condition",
            new RecordTrackConditionRequest(
                DateTimeOffset.UtcNow.AddDays(-1),
                TurfConditionCode: "GOOD",
                DirtConditionCode: null,
                GoingDescriptionText: "良"),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, satTrack.StatusCode, "土曜馬場状態観測に失敗");

        // --- 調教後の最終確認メモを記録する ---
        var memo3Response = await _client.PostAsJsonAsync("/api/memos",
            new CreateMemoRequest(
                AuthorId: "analyst-1",
                MemoType: "TrainingNote",
                Content: "サニーブレイズ最終追い切りで馬なりで余裕十分。状態は申し分なし。当日は晴れ予報で馬場は良が続く見込み。",
                CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
                Subjects: new[]
                {
                    new MemoSubjectDto("Horse", horse1Id),
                    new MemoSubjectDto("Race", raceId)
                },
                MemoId: memo3Id),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.Created, memo3Response.StatusCode, "土曜調教メモの記録に失敗");

        // --- 予想チケットを確定する ---
        var finalizeTicket = await _client.PostAsync($"/api/predictions/{ticketId}/finalize", null);
        Assert.AreEqual(HttpStatusCode.OK, finalizeTicket.StatusCode, "予想チケットの確定に失敗");

        // ═══════════════════════════════════════════════════
        // 【日曜日】レース当日
        // ═══════════════════════════════════════════════════

        // --- 天気を観測する（日曜朝） ---
        var sunWeather = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/weather",
            new RecordWeatherObservationRequest(
                DateTimeOffset.UtcNow,
                WeatherCode: "SUNNY",
                WeatherText: "晴れ",
                TemperatureCelsius: 23.0m,
                HumidityPercent: 55.0m,
                WindDirectionCode: "S",
                WindSpeedMeterPerSecond: 2.5m),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, sunWeather.StatusCode, "日曜天気観測に失敗");

        // --- 馬場状態を観測する（日曜朝） ---
        var sunTrack = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/track-condition",
            new RecordTrackConditionRequest(
                DateTimeOffset.UtcNow,
                TurfConditionCode: "GOOD",
                DirtConditionCode: null,
                GoingDescriptionText: "良"),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, sunTrack.StatusCode, "日曜馬場状態観測に失敗");

        // --- レース直前を開く（PreRaceOpen 状態に遷移） ---
        var openPreRace = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/open-pre-race", (object?)null, JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, openPreRace.StatusCode, "PreRaceOpen 遷移に失敗");

        var racePreRace = await (await _client.GetAsync($"/api/races/{raceId}"))
            .Content.ReadFromJsonAsync<RaceResponse>(JsonOptions);
        Assert.IsNotNull(racePreRace);
        Assert.AreEqual(RaceStatus.PreRaceOpen, racePreRace.Status, "PreRaceOpen 状態になるべき");

        // --- レース開始（InProgress 状態に遷移） ---
        var startRace = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/start", (object?)null, JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, startRace.StatusCode, "レース開始に失敗");

        var raceInProgress = await (await _client.GetAsync($"/api/races/{raceId}"))
            .Content.ReadFromJsonAsync<RaceResponse>(JsonOptions);
        Assert.IsNotNull(raceInProgress);
        Assert.AreEqual(RaceStatus.InProgress, raceInProgress.Status, "InProgress 状態になるべき");

        // --- レース結果を宣言する ---
        var declaredAt = DateTimeOffset.UtcNow;
        var declareResult = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/result",
            new DeclareRaceResultRequest("サニーブレイズ", declaredAt),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, declareResult.StatusCode, "レース結果宣言に失敗");

        var raceResult = await (await _client.GetAsync($"/api/races/{raceId}"))
            .Content.ReadFromJsonAsync<RaceResponse>(JsonOptions);
        Assert.IsNotNull(raceResult);
        Assert.AreEqual(RaceStatus.ResultDeclared, raceResult.Status, "ResultDeclared 状態になるべき");
        Assert.AreEqual("サニーブレイズ", raceResult.WinningHorseName, "優勝馬名が一致すべき");

        // --- 各馬のエントリー結果を宣言する ---
        var entryResult1 = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries/{entry1WithGate}/result",
            new DeclareEntryResultRequest(1, "2:23.4", null, "34.8", null, null),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, entryResult1.StatusCode, "エントリー1の結果宣言に失敗");

        var entryResult2 = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries/{entry2WithGate}/result",
            new DeclareEntryResultRequest(3, "2:23.9", "3/4", "35.1", null, null),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, entryResult2.StatusCode, "エントリー2の結果宣言に失敗");

        var entryResult3 = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/entries/{entry3WithGate}/result",
            new DeclareEntryResultRequest(5, "2:24.2", "1.3/4", "35.4", null, null),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, entryResult3.StatusCode, "エントリー3の結果宣言に失敗");

        // --- 払戻結果を宣言する ---
        var declarePayout = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/payout",
            new DeclarePayoutResultRequest(
                DeclaredAt: declaredAt,
                WinPayouts: new[] { new PayoutEntryDto("4", 320m) },
                PlacePayouts: new[]
                {
                    new PayoutEntryDto("4", 140m),
                    new PayoutEntryDto("7", 210m),
                    new PayoutEntryDto("11", 180m)
                },
                QuinellaPayouts: new[] { new PayoutEntryDto("4-7", 1420m) },
                ExactaPayouts: new[] { new PayoutEntryDto("4-7", 2380m) },
                TrifectaPayouts: null),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, declarePayout.StatusCode, "払戻結果宣言に失敗");

        // --- レースをクローズする ---
        var closeRace = await _client.PostAsJsonAsync(
            $"/api/races/{raceId}/close", (object?)null, JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, closeRace.StatusCode, "レースクローズに失敗");

        var raceClosed = await (await _client.GetAsync($"/api/races/{raceId}"))
            .Content.ReadFromJsonAsync<RaceResponse>(JsonOptions);
        Assert.IsNotNull(raceClosed);
        Assert.AreEqual(RaceStatus.Closed, raceClosed.Status, "Closed 状態になるべき");

        // ═══════════════════════════════════════════════════
        // 【事後】予想評価
        // ═══════════════════════════════════════════════════

        // --- 予想チケットを評価する（本命的中・馬連的中） ---
        var evaluateTicket = await _client.PostAsJsonAsync(
            $"/api/predictions/{ticketId}/evaluate",
            new EvaluatePredictionTicketRequest(
                RaceId: raceId,
                EvaluatedAt: DateTimeOffset.UtcNow,
                EvaluationRevision: 1,
                HitTypeCodes: new[] { "WIN", "EXACTA" },
                ScoreSummary: 95.0m,
                ReturnAmount: 2380m,
                Roi: 1.19m),
            JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, evaluateTicket.StatusCode, "予想評価に失敗");

        // --- 予想比較ビューを確認する ---
        var comparisonResponse = await _client.GetAsync($"/api/races/{raceId}/comparison");
        Assert.AreEqual(HttpStatusCode.OK, comparisonResponse.StatusCode, "予想比較ビュー取得に失敗");

        // --- 馬に紐づくメモが正しく取得できることを確認する ---
        var horse1Memos = await _client.GetAsync($"/api/memos/by-subject/Horse/{horse1Id}");
        Assert.AreEqual(HttpStatusCode.OK, horse1Memos.StatusCode, "馬メモ取得に失敗");
        var horse1MemosJson = await horse1Memos.Content.ReadAsStringAsync();
        using var horse1MemosDoc = JsonDocument.Parse(horse1MemosJson);
        var horse1MemosArray = horse1MemosDoc.RootElement.EnumerateArray().ToList();
        Assert.IsTrue(horse1MemosArray.Count >= 2, "本命馬には少なくとも2件のメモが記録されているべき");
    }
}
