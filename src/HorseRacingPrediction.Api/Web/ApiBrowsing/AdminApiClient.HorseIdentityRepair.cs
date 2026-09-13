using System.Net.Http.Json;
using HorseRacingPrediction.Api.Contracts;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    private const string HorseIdentityRepairPath = "/api/admin/repairs/20260913-jra-horse-identity";

    public Task<HorseIdentityRepairPreviewResponse?> GetHorseIdentityRepairPreviewAsync(
        CancellationToken token = default)
        => GetJsonAsync<HorseIdentityRepairPreviewResponse>(HorseIdentityRepairPath, token);

    public async Task<AdminApiResult<ApplyHorseIdentityRepairResponse>> ApplyHorseIdentityRepairAsync(
        IReadOnlyList<string> candidateIds, CancellationToken token = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{HorseIdentityRepairPath}/apply", new ApplyHorseIdentityRepairRequest(candidateIds),
            JsonOptions, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return AdminApiResult<ApplyHorseIdentityRepairResponse>.Fail(
                await ReadErrorsAsync(response, token).ConfigureAwait(false));
        var value = await response.Content.ReadFromJsonAsync<ApplyHorseIdentityRepairResponse>(
            JsonOptions, token).ConfigureAwait(false);
        return value is null
            ? AdminApiResult<ApplyHorseIdentityRepairResponse>.Fail(["補正結果を確認できませんでした。"])
            : AdminApiResult<ApplyHorseIdentityRepairResponse>.Ok(value);
    }
}
