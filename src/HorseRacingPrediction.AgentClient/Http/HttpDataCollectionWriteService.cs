using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HorseRacingPrediction.Agents.Contracts;
using HorseRacingPrediction.Agents.Plugins;
using HorseRacingPrediction.AgentClient.Scheduling;

namespace HorseRacingPrediction.AgentClient.Http;

/// <summary>
/// クラウド API を呼び出して <see cref="IDataCollectionWriteService"/> を実装するクラス。
/// <para>
/// 馬・騎手・調教師の Upsert は決定論的 ID 生成（UUID v5 相当）を使い、
/// GET で存在確認してから POST または PUT を呼び分ける。
/// </para>
/// </summary>
public sealed class HttpDataCollectionWriteService : IDataCollectionWriteService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AgentAcquisitionStatusRecorder _statusRecorder;

    public HttpDataCollectionWriteService(HttpClient httpClient, AgentAcquisitionStatusRecorder statusRecorder)
    {
        _httpClient = httpClient;
        _statusRecorder = statusRecorder;
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
        ValidateRequiredText(raceDate, nameof(raceDate));
        ValidateRequiredText(racecourseCode, nameof(racecourseCode));
        ValidateRequiredText(raceName, nameof(raceName));
        if (raceNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(raceNumber), raceNumber, "raceNumber must be greater than zero.");
        }

        var parsedRaceDate = DateOnly.Parse(raceDate, CultureInfo.InvariantCulture);
        var raceId = DeterministicIdGenerator.BuildRaceId(parsedRaceDate, racecourseCode, raceNumber);

        var existing = await GetRacePredictionContextAsync(raceId, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            var createRequest = new
            {
                RaceId = raceId,
                RaceDate = parsedRaceDate,
                RacecourseCode = racecourseCode,
                RaceNumber = raceNumber,
                RaceName = raceName,
                GradeCode = gradeCode,
                SurfaceCode = surfaceCode,
                DistanceMeters = distanceMeters,
                DirectionCode = directionCode
            };
            var createResponse = await _httpClient
                .PostAsJsonAsync("/api/races", createRequest, cancellationToken)
                .ConfigureAwait(false);

            if (createResponse.StatusCode == HttpStatusCode.Conflict)
            {
                await CorrectRaceAsync(
                    raceId,
                    raceName,
                    racecourseCode,
                    raceNumber,
                    gradeCode,
                    surfaceCode,
                    distanceMeters,
                    directionCode,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                createResponse.EnsureSuccessStatusCode();
            }
        }
        else
        {
            await CorrectRaceAsync(
                raceId,
                raceName,
                racecourseCode,
                raceNumber,
                gradeCode,
                surfaceCode,
                distanceMeters,
                directionCode,
                cancellationToken).ConfigureAwait(false);
        }

        if (entryCount is > 0 && (existing is null || existing.Status == RaceStatus.Draft))
        {
            var publishRequest = new { EntryCount = entryCount.Value };
            var publishResponse = await _httpClient
                .PostAsJsonAsync($"/api/races/{Uri.EscapeDataString(raceId)}/card/publish", publishRequest, cancellationToken)
                .ConfigureAwait(false);
            if (publishResponse.StatusCode != HttpStatusCode.Conflict)
            {
                publishResponse.EnsureSuccessStatusCode();
            }
        }

        return raceId;
    }

    private async Task CorrectRaceAsync(
        string raceId,
        string raceName,
        string racecourseCode,
        int raceNumber,
        string? gradeCode,
        string? surfaceCode,
        int? distanceMeters,
        string? directionCode,
        CancellationToken cancellationToken)
    {
        var correctRequest = new
        {
            RaceName = raceName,
            RacecourseCode = racecourseCode,
            RaceNumber = (int?)raceNumber,
            GradeCode = gradeCode,
            SurfaceCode = surfaceCode,
            DistanceMeters = distanceMeters,
            DirectionCode = directionCode,
            Reason = "Collected by data collection agent"
        };
        var patchResponse = await _httpClient
            .PatchAsJsonAsync($"/api/races/{Uri.EscapeDataString(raceId)}", correctRequest, cancellationToken)
            .ConfigureAwait(false);
        patchResponse.EnsureSuccessStatusCode();
    }

    public async Task<string> UpsertHorseAsync(
        string registeredName,
        string? normalizedName,
        string? sexCode,
        string? birthDate,
        CancellationToken cancellationToken = default)
    {
        ValidateRequiredText(registeredName, nameof(registeredName));

        try
        {
            var normalized = DeterministicIdGenerator.NormalizeDisplayName(normalizedName ?? registeredName);
            var horseId = DeterministicIdGenerator.BuildEntityId("horse", normalized);
            var parsedBirthDate = TryParseDateOnly(birthDate);

            var existing = await GetAsync<HorseExistenceDto>($"/api/horses/{Uri.EscapeDataString(horseId)}", cancellationToken).ConfigureAwait(false);

            if (existing is null)
            {
                var registerRequest = new
                {
                    HorseId = horseId,
                    RegisteredName = registeredName,
                    NormalizedName = normalized,
                    SexCode = sexCode,
                    BirthDate = parsedBirthDate
                };
                var response = await _httpClient
                    .PostAsJsonAsync("/api/horses", registerRequest, cancellationToken)
                    .ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    await UpdateHorseAsync(horseId, registeredName, normalized, sexCode, parsedBirthDate, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                }
            }
            else
            {
                await UpdateHorseAsync(horseId, registeredName, normalized, sexCode, parsedBirthDate, cancellationToken).ConfigureAwait(false);
            }

            await _statusRecorder.RecordAsync(
                AgentAcquisitionSubjectType.Horse,
                AgentAcquisitionOperationType.EntityUpsert,
                registeredName,
                RaceDataCollectionState.Succeeded,
                providerType: "API",
                subjectId: horseId,
                relatedRaceId: null,
                sourceUrl: null,
                errorCode: null,
                errorReason: null,
                cancellationToken).ConfigureAwait(false);

            return horseId;
        }
        catch (Exception ex)
        {
            var error = RaceDataCollectionErrorClassifier.Classify(ex.Message, ex);
            await _statusRecorder.RecordAsync(
                AgentAcquisitionSubjectType.Horse,
                AgentAcquisitionOperationType.EntityUpsert,
                registeredName,
                RaceDataCollectionState.Failed,
                providerType: "API",
                subjectId: null,
                relatedRaceId: null,
                sourceUrl: null,
                error.Code,
                error.Reason,
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<string> UpsertJockeyAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default)
    {
        ValidateRequiredText(displayName, nameof(displayName));

        try
        {
            var normalized = DeterministicIdGenerator.NormalizeDisplayName(normalizedName ?? displayName);
            var jockeyId = DeterministicIdGenerator.BuildEntityId("jockey", normalized);

            var existing = await GetAsync<JockeyExistenceDto>($"/api/jockeys/{Uri.EscapeDataString(jockeyId)}", cancellationToken).ConfigureAwait(false);

            if (existing is null)
            {
                var registerRequest = new
                {
                    JockeyId = jockeyId,
                    DisplayName = displayName,
                    NormalizedName = normalized,
                    AffiliationCode = affiliationCode
                };
                var response = await _httpClient
                    .PostAsJsonAsync("/api/jockeys", registerRequest, cancellationToken)
                    .ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    await UpdateJockeyAsync(jockeyId, displayName, normalized, affiliationCode, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                }
            }
            else
            {
                await UpdateJockeyAsync(jockeyId, displayName, normalized, affiliationCode, cancellationToken).ConfigureAwait(false);
            }

            await _statusRecorder.RecordAsync(
                AgentAcquisitionSubjectType.Jockey,
                AgentAcquisitionOperationType.EntityUpsert,
                displayName,
                RaceDataCollectionState.Succeeded,
                providerType: "API",
                subjectId: jockeyId,
                relatedRaceId: null,
                sourceUrl: null,
                errorCode: null,
                errorReason: null,
                cancellationToken).ConfigureAwait(false);

            return jockeyId;
        }
        catch (Exception ex)
        {
            var error = RaceDataCollectionErrorClassifier.Classify(ex.Message, ex);
            await _statusRecorder.RecordAsync(
                AgentAcquisitionSubjectType.Jockey,
                AgentAcquisitionOperationType.EntityUpsert,
                displayName,
                RaceDataCollectionState.Failed,
                providerType: "API",
                subjectId: null,
                relatedRaceId: null,
                sourceUrl: null,
                error.Code,
                error.Reason,
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<string> UpsertTrainerAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default)
    {
        ValidateRequiredText(displayName, nameof(displayName));

        try
        {
            var normalized = DeterministicIdGenerator.NormalizeDisplayName(normalizedName ?? displayName);
            var trainerId = DeterministicIdGenerator.BuildEntityId("trainer", normalized);

            var existing = await GetAsync<TrainerExistenceDto>($"/api/trainers/{Uri.EscapeDataString(trainerId)}", cancellationToken).ConfigureAwait(false);

            if (existing is null)
            {
                var registerRequest = new
                {
                    TrainerId = trainerId,
                    DisplayName = displayName,
                    NormalizedName = normalized,
                    AffiliationCode = affiliationCode
                };
                var response = await _httpClient
                    .PostAsJsonAsync("/api/trainers", registerRequest, cancellationToken)
                    .ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    await UpdateTrainerAsync(trainerId, displayName, normalized, affiliationCode, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                }
            }
            else
            {
                await UpdateTrainerAsync(trainerId, displayName, normalized, affiliationCode, cancellationToken).ConfigureAwait(false);
            }

            await _statusRecorder.RecordAsync(
                AgentAcquisitionSubjectType.Trainer,
                AgentAcquisitionOperationType.EntityUpsert,
                displayName,
                RaceDataCollectionState.Succeeded,
                providerType: "API",
                subjectId: trainerId,
                relatedRaceId: null,
                sourceUrl: null,
                errorCode: null,
                errorReason: null,
                cancellationToken).ConfigureAwait(false);

            return trainerId;
        }
        catch (Exception ex)
        {
            var error = RaceDataCollectionErrorClassifier.Classify(ex.Message, ex);
            await _statusRecorder.RecordAsync(
                AgentAcquisitionSubjectType.Trainer,
                AgentAcquisitionOperationType.EntityUpsert,
                displayName,
                RaceDataCollectionState.Failed,
                providerType: "API",
                subjectId: null,
                relatedRaceId: null,
                sourceUrl: null,
                error.Code,
                error.Reason,
                cancellationToken).ConfigureAwait(false);
            throw;
        }
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
        ValidateRequiredText(raceId, nameof(raceId));

        if (horseNumber <= 0)
        {
            return $"レース {raceId} の出走登録をスキップしました（馬番未取得）。";
        }

        ValidateRequiredText(horseName, nameof(horseName));
        ValidateRequiredText(trainerName, nameof(trainerName));

        var race = await GetRacePredictionContextAsync(raceId, cancellationToken).ConfigureAwait(false);
        var existingEntry = race?.Entries.FirstOrDefault(e => e.HorseNumber == horseNumber);
        if (existingEntry is not null)
        {
            // 既存エントリの場合でも、関連エンティティ欠落や名称欠落を補完する。
            await EnsureHorseExistsByIdAsync(existingEntry.HorseId, horseName, sexCode, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(existingEntry.JockeyId))
            {
                await EnsureJockeyExistsByIdAsync(existingEntry.JockeyId, jockeyName, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(existingEntry.TrainerId))
            {
                await EnsureTrainerExistsByIdAsync(existingEntry.TrainerId, trainerName, cancellationToken).ConfigureAwait(false);
            }

            return $"レース {raceId} の馬番 {horseNumber} は既に登録済みです（関連エンティティを補完しました）。";
        }

        var horseId = await UpsertHorseAsync(horseName, normalizedName: null, sexCode: sexCode, birthDate: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        var jockeyId = string.IsNullOrWhiteSpace(jockeyName)
            ? null
            : await UpsertJockeyAsync(jockeyName, null, null, cancellationToken).ConfigureAwait(false);
        var trainerId = string.IsNullOrWhiteSpace(trainerName)
            ? null
            : await UpsertTrainerAsync(trainerName, null, null, cancellationToken).ConfigureAwait(false);

        var entryId = DeterministicIdGenerator.BuildRaceEntryId(raceId, horseNumber);
        var registerRequest = new
        {
            EntryId = entryId,
            HorseId = horseId,
            HorseNumber = horseNumber,
            JockeyId = jockeyId,
            TrainerId = trainerId,
            HorseName = horseName,
            JockeyName = jockeyName,
            TrainerName = trainerName,
            GateNumber = gateNumber,
            AssignedWeight = assignedWeight,
            SexCode = sexCode,
            Age = age,
            DeclaredWeight = declaredWeight,
            DeclaredWeightDiff = declaredWeightDiff
        };

        var response = await _httpClient
            .PostAsJsonAsync($"/api/races/{Uri.EscapeDataString(raceId)}/entries", registerRequest, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return $"レース {raceId} の馬番 {horseNumber} は既に登録済みです。";
        }

        response.EnsureSuccessStatusCode();

        return $"レース {raceId} に馬番 {horseNumber} の出走登録を行いました。";
    }

    public async Task<string> DeclareRaceResultAsync(
        string raceId,
        string winningHorseName,
        string? declaredAt,
        string? winningHorseId,
        CancellationToken cancellationToken = default)
    {
        ValidateRequiredText(raceId, nameof(raceId));
        ValidateRequiredText(winningHorseName, nameof(winningHorseName));

        var race = await GetRacePredictionContextAsync(raceId, cancellationToken).ConfigureAwait(false);
        if (race?.Status == RaceStatus.Draft)
        {
            var entryCount = race.Entries.Count > 0 ? race.Entries.Count : 1;
            var publishRequest = new { EntryCount = entryCount };
            var publishResponse = await _httpClient
                .PostAsJsonAsync($"/api/races/{Uri.EscapeDataString(raceId)}/card/publish", publishRequest, cancellationToken)
                .ConfigureAwait(false);
            if (publishResponse.StatusCode != HttpStatusCode.Conflict)
            {
                publishResponse.EnsureSuccessStatusCode();
            }
        }

        var resultRequest = new
        {
            WinningHorseName = winningHorseName,
            DeclaredAt = declaredAt is not null
                ? (DateTimeOffset?)DateTimeOffset.Parse(declaredAt, CultureInfo.InvariantCulture)
                : null
        };
        var resultResponse = await _httpClient
            .PostAsJsonAsync($"/api/races/{Uri.EscapeDataString(raceId)}/result", resultRequest, cancellationToken)
            .ConfigureAwait(false);

        if (resultResponse.StatusCode == HttpStatusCode.Conflict)
        {
            return $"レース {raceId} の確定結果は既に記録済みです。";
        }

        resultResponse.EnsureSuccessStatusCode();

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
        ValidateRequiredText(raceId, nameof(raceId));
        if (horseNumber <= 0)
        {
            return $"レース {raceId} の成績登録をスキップしました（馬番未取得）。";
        }

        var entryId = DeterministicIdGenerator.BuildRaceEntryId(raceId, horseNumber);
        var request = new
        {
            FinishPosition = finishPosition,
            OfficialTime = officialTime,
            MarginText = marginText,
            LastThreeFurlongTime = lastThreeFurlongTime,
            AbnormalResultCode = abnormalResultCode,
            PrizeMoney = prizeMoney
        };

        var response = await _httpClient
            .PostAsJsonAsync($"/api/races/{Uri.EscapeDataString(raceId)}/entries/{Uri.EscapeDataString(entryId)}/result", request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return $"レース {raceId} の馬番 {horseNumber} の成績は既に記録済みです。";
        }

        response.EnsureSuccessStatusCode();

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
        ValidateRequiredText(raceId, nameof(raceId));

        var request = new
        {
            DeclaredAt = DateTimeOffset.UtcNow,
            WinPayouts = ParsePayoutsForRequest(winPayoutsJson),
            PlacePayouts = ParsePayoutsForRequest(placePayoutsJson),
            QuinellaPayouts = ParsePayoutsForRequest(quinellaPayoutsJson),
            ExactaPayouts = ParsePayoutsForRequest(exactaPayoutsJson),
            TrifectaPayouts = ParsePayoutsForRequest(trifectaPayoutsJson)
        };

        var response = await _httpClient
            .PostAsJsonAsync($"/api/races/{Uri.EscapeDataString(raceId)}/payout", request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return $"レース {raceId} の払い戻しは既に記録済みです。";
        }

        response.EnsureSuccessStatusCode();

        return $"レース {raceId} の払い戻しを記録しました。";
    }

    // ------------------------------------------------------------------ //
    // private helpers — HTTP
    // ------------------------------------------------------------------ //

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        ValidateRequiredText(path, nameof(path));

        var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureHorseExistsByIdAsync(
        string horseId,
        string? horseName,
        string? sexCode,
        CancellationToken cancellationToken)
    {
        var existing = await GetAsync<HorseExistenceDto>($"/api/horses/{Uri.EscapeDataString(horseId)}", cancellationToken).ConfigureAwait(false);
        var resolvedName = string.IsNullOrWhiteSpace(horseName) ? horseId : horseName.Trim();
        var normalizedName = DeterministicIdGenerator.NormalizeDisplayName(resolvedName);

        if (existing is null)
        {
            var registerRequest = new
            {
                HorseId = horseId,
                RegisteredName = resolvedName,
                NormalizedName = normalizedName,
                SexCode = sexCode,
                BirthDate = (DateOnly?)null
            };
            var response = await _httpClient
                .PostAsJsonAsync("/api/horses", registerRequest, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                await UpdateHorseAsync(horseId, resolvedName, normalizedName, sexCode, null, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                response.EnsureSuccessStatusCode();
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(horseName))
        {
            await UpdateHorseAsync(horseId, resolvedName, normalizedName, sexCode, null, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureJockeyExistsByIdAsync(
        string jockeyId,
        string? jockeyName,
        CancellationToken cancellationToken)
    {
        var existing = await GetAsync<JockeyExistenceDto>($"/api/jockeys/{Uri.EscapeDataString(jockeyId)}", cancellationToken).ConfigureAwait(false);
        var resolvedName = string.IsNullOrWhiteSpace(jockeyName) ? jockeyId : jockeyName.Trim();
        var normalizedName = DeterministicIdGenerator.NormalizeDisplayName(resolvedName);

        if (existing is null)
        {
            var registerRequest = new
            {
                JockeyId = jockeyId,
                DisplayName = resolvedName,
                NormalizedName = normalizedName,
                AffiliationCode = (string?)null
            };
            var response = await _httpClient
                .PostAsJsonAsync("/api/jockeys", registerRequest, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                await UpdateJockeyAsync(jockeyId, resolvedName, normalizedName, null, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                response.EnsureSuccessStatusCode();
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(jockeyName))
        {
            await UpdateJockeyAsync(jockeyId, resolvedName, normalizedName, null, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureTrainerExistsByIdAsync(
        string trainerId,
        string? trainerName,
        CancellationToken cancellationToken)
    {
        var existing = await GetAsync<TrainerExistenceDto>($"/api/trainers/{Uri.EscapeDataString(trainerId)}", cancellationToken).ConfigureAwait(false);
        var resolvedName = string.IsNullOrWhiteSpace(trainerName) ? trainerId : trainerName.Trim();
        var normalizedName = DeterministicIdGenerator.NormalizeDisplayName(resolvedName);

        if (existing is null)
        {
            var registerRequest = new
            {
                TrainerId = trainerId,
                DisplayName = resolvedName,
                NormalizedName = normalizedName,
                AffiliationCode = (string?)null
            };
            var response = await _httpClient
                .PostAsJsonAsync("/api/trainers", registerRequest, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                await UpdateTrainerAsync(trainerId, resolvedName, normalizedName, null, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                response.EnsureSuccessStatusCode();
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(trainerName))
        {
            await UpdateTrainerAsync(trainerId, resolvedName, normalizedName, null, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<RacePredictionContextReadModel?> GetRacePredictionContextAsync(string raceId, CancellationToken cancellationToken)
        => await GetAsync<RacePredictionContextReadModel>($"/api/races/{Uri.EscapeDataString(raceId)}/context", cancellationToken).ConfigureAwait(false);

    // ------------------------------------------------------------------ //
    // private helpers — payout parsing
    // ------------------------------------------------------------------ //

    private static List<PayoutEntry>? ParsePayoutsForRequest(string? json)
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

    private async Task UpdateHorseAsync(
        string horseId,
        string registeredName,
        string normalizedName,
        string? sexCode,
        DateOnly? birthDate,
        CancellationToken cancellationToken)
    {
        var updateRequest = new
        {
            RegisteredName = registeredName,
            NormalizedName = normalizedName,
            SexCode = sexCode,
            BirthDate = birthDate
        };
        var response = await _httpClient
            .PutAsJsonAsync($"/api/horses/{Uri.EscapeDataString(horseId)}", updateRequest, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task UpdateJockeyAsync(
        string jockeyId,
        string displayName,
        string normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken)
    {
        var updateRequest = new
        {
            DisplayName = displayName,
            NormalizedName = normalizedName,
            AffiliationCode = affiliationCode
        };
        var response = await _httpClient
            .PutAsJsonAsync($"/api/jockeys/{Uri.EscapeDataString(jockeyId)}", updateRequest, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task UpdateTrainerAsync(
        string trainerId,
        string displayName,
        string normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken)
    {
        var updateRequest = new
        {
            DisplayName = displayName,
            NormalizedName = normalizedName,
            AffiliationCode = affiliationCode
        };
        var response = await _httpClient
            .PutAsJsonAsync($"/api/trainers/{Uri.EscapeDataString(trainerId)}", updateRequest, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private sealed record PayoutEntry(string Combination, decimal Amount);

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
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static void ValidateRequiredText(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }
    }

    // ------------------------------------------------------------------ //
    // private DTO — existence check only
    // ------------------------------------------------------------------ //

    private sealed class HorseExistenceDto { public string HorseId { get; init; } = string.Empty; }
    private sealed class JockeyExistenceDto { public string JockeyId { get; init; } = string.Empty; }
    private sealed class TrainerExistenceDto { public string TrainerId { get; init; } = string.Empty; }
}
