using System.Net.Http.Json;
using HorseRacingPrediction.Api.Contracts;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    private const string SubjectIdentificationRepairPath = "/api/admin/repairs/subject-identification";

    public Task<SubjectIdentificationRepairPreviewResponse?> GetSubjectIdentificationRepairPreviewAsync(
        CancellationToken cancellationToken = default)
        => GetJsonAsync<SubjectIdentificationRepairPreviewResponse>(SubjectIdentificationRepairPath, cancellationToken);

    public async Task<AdminApiResult<ExecuteSubjectIdentificationRepairResponse>>
        ExecuteSubjectIdentificationRepairAsync(
            IReadOnlyList<ExecuteSubjectIdentificationRepairItem> items,
            CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{SubjectIdentificationRepairPath}/execute",
            new ExecuteSubjectIdentificationRepairRequest(items), JsonOptions, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return AdminApiResult<ExecuteSubjectIdentificationRepairResponse>.Fail(
                await ReadErrorsAsync(response, cancellationToken).ConfigureAwait(false));

        var value = await response.Content.ReadFromJsonAsync<ExecuteSubjectIdentificationRepairResponse>(
            JsonOptions, cancellationToken).ConfigureAwait(false);
        return value is null
            ? AdminApiResult<ExecuteSubjectIdentificationRepairResponse>.Fail(["補正結果を確認できませんでした。"])
            : AdminApiResult<ExecuteSubjectIdentificationRepairResponse>.Ok(value);
    }
}
