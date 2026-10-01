using System.Net.Http.Json;

using HorseRacingPrediction.Contracts.Repairs;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    private const string HorseIdentityRepairPath = "/api/admin/repairs/20260913-jra-horse-identity";

    public Task<HorseIdentityRepairPreviewDto?> GetHorseIdentityRepairPreviewAsync(
        CancellationToken token = default)
        => GetHorseIdentityRepairPreviewCoreAsync(token);

    private async Task<HorseIdentityRepairPreviewDto?> GetHorseIdentityRepairPreviewCoreAsync(CancellationToken token)
        => (await GetJsonAsync<GetHorseIdentityRepairResponse>(HorseIdentityRepairPath, token).ConfigureAwait(false))?.Preview;

    public async Task<AdminApiResult<HorseIdentityRepairApplicationDto>> ApplyHorseIdentityRepairAsync(
        IReadOnlyList<string> candidateIds, CancellationToken token = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{HorseIdentityRepairPath}/apply", new ApplyHorseIdentityRepairRequest(candidateIds),
            JsonOptions, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return AdminApiResult<HorseIdentityRepairApplicationDto>.Fail(
                await ReadErrorsAsync(response, token).ConfigureAwait(false));
        var value = await response.Content.ReadFromJsonAsync<ApplyHorseIdentityRepairResponse>(
            JsonOptions, token).ConfigureAwait(false);
        return value is null
            ? AdminApiResult<HorseIdentityRepairApplicationDto>.Fail(["補正結果を確認できませんでした。"])
            : AdminApiResult<HorseIdentityRepairApplicationDto>.Ok(value.Application);
    }
}
