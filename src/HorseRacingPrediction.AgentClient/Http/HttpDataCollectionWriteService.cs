using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HorseRacingPrediction.Agents.Plugins;

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

    public HttpDataCollectionWriteService(HttpClient httpClient)
    {
        _httpClient = httpClient;
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
        var raceId = DeterministicIdGenerator.BuildRaceId(parsedRaceDate, racecourseCode, raceNumber).Value;

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
            createResponse.EnsureSuccessStatusCode();
        }
        else
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

        if (entryCount is > 0 && existing is null)
        {
            var publishRequest = new { EntryCount = entryCount.Value };
            var publishResponse = await _httpClient
                .PostAsJsonAsync($"/api/races/{Uri.EscapeDataString(raceId)}/card/publish", publishRequest, cancellationToken)
                .ConfigureAwait(false);
            publishResponse.EnsureSuccessStatusCode();
        }

        return raceId;
    }

    public async Task<string> UpsertHorseAsync(
        string registeredName,
        string? normalizedName,
        string? sexCode,
        string? birthDate,
        CancellationToken cancellationToken = default)
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
            response.EnsureSuccessStatusCode();
        }
        else
        {
            var updateRequest = new
            {
                RegisteredName = registeredName,
                NormalizedName = normalized,
                SexCode = sexCode,
                BirthDate = parsedBirthDate
            };
            var response = await _httpClient
                .PutAsJsonAsync($"/api/horses/{Uri.EscapeDataString(horseId)}", updateRequest, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        return horseId;
    }

    public async Task<string> UpsertJockeyAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default)
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
            response.EnsureSuccessStatusCode();
        }
        else
        {
            var updateRequest = new
            {
                DisplayName = displayName,
                NormalizedName = normalized,
                AffiliationCode = affiliationCode
            };
            var response = await _httpClient
                .PutAsJsonAsync($"/api/jockeys/{Uri.EscapeDataString(jockeyId)}", updateRequest, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        return jockeyId;
    }

    public async Task<string> UpsertTrainerAsync(
        string displayName,
        string? normalizedName,
        string? affiliationCode,
        CancellationToken cancellationToken = default)
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
            response.EnsureSuccessStatusCode();
        }
        else
        {
            var updateRequest = new
            {
                DisplayName = displayName,
                NormalizedName = normalized,
                AffiliationCode = affiliationCode
            };
            var response = await _httpClient
                .PutAsJsonAsync($"/api/trainers/{Uri.EscapeDataString(trainerId)}", updateRequest, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        return trainerId;
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
        var race = await GetRacePredictionContextAsync(raceId, cancellationToken).ConfigureAwait(false);
        if (race is null)
            throw new InvalidOperationException($"レースが存在しません: {raceId}");

        var entries = race.Entries;
        if (entries.Any(e => e.HorseNumber == horseNumber))
            return $"レース {raceId} の馬番 {horseNumber} は既に登録済みです。";

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
        var race = await GetRacePredictionContextAsync(raceId, cancellationToken).ConfigureAwait(false);
        if (race is null)
            throw new InvalidOperationException($"レースが存在しません: {raceId}");

        if (race.Status == HorseRacingPrediction.Domain.Races.RaceStatus.Draft)
        {
            var entryCount = race.Entries.Count > 0 ? race.Entries.Count : 1;
            var publishRequest = new { EntryCount = entryCount };
            var publishResponse = await _httpClient
                .PostAsJsonAsync($"/api/races/{Uri.EscapeDataString(raceId)}/card/publish", publishRequest, cancellationToken)
                .ConfigureAwait(false);
            publishResponse.EnsureSuccessStatusCode();
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
        response.EnsureSuccessStatusCode();

        return $"レース {raceId} の払い戻しを記録しました。";
    }

    // ------------------------------------------------------------------ //
    // private helpers — HTTP
    // ------------------------------------------------------------------ //

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RacePredictionContextDto?> GetRacePredictionContextAsync(string raceId, CancellationToken cancellationToken)
        => await GetAsync<RacePredictionContextDto>($"/api/races/{Uri.EscapeDataString(raceId)}/context", cancellationToken).ConfigureAwait(false);

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

    // ------------------------------------------------------------------ //
    // private DTO — existence check only
    // ------------------------------------------------------------------ //

    private sealed class HorseExistenceDto { public string HorseId { get; init; } = string.Empty; }
    private sealed class JockeyExistenceDto { public string JockeyId { get; init; } = string.Empty; }
    private sealed class TrainerExistenceDto { public string TrainerId { get; init; } = string.Empty; }
}
