using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Repairs;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    public Task<SubjectNameNormalizationPageDto?> SearchSubjectNameNormalizationAsync(
        CollectionResourceType subjectType, string query, int page, int pageSize = 25,
        CancellationToken token = default) => SearchSubjectNameNormalizationCoreAsync(subjectType, query, page, pageSize, token);

    private async Task<SubjectNameNormalizationPageDto?> SearchSubjectNameNormalizationCoreAsync(
        CollectionResourceType subjectType, string query, int page, int pageSize, CancellationToken token)
        => (await GetJsonAsync<GetSubjectNameNormalizationResponse>(
            $"/api/admin/repairs/subject-name-normalization?subjectType={subjectType}&query={Uri.EscapeDataString(query)}&page={page}&pageSize={pageSize}",
            token).ConfigureAwait(false))?.Page;

    public async Task<AdminApiResult<SubjectNameNormalizationApplyResultDto>> ApplySubjectNameNormalizationAsync(
        ApplySubjectNameNormalizationRequest request, CancellationToken token = default)
    {
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/admin/repairs/subject-name-normalization/apply")
        { Content = System.Net.Http.Json.JsonContent.Create(request, options: JsonOptions) };
        using var response = await _httpClient.SendAsync(requestMessage, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return AdminApiResult<SubjectNameNormalizationApplyResultDto>.Fail(await ReadErrorsAsync(response, token).ConfigureAwait(false));
        var value = await response.Content.ReadFromJsonAsync<ApplySubjectNameNormalizationResponse>(JsonOptions, token).ConfigureAwait(false);
        return value is null
            ? AdminApiResult<SubjectNameNormalizationApplyResultDto>.Fail(["応答の解析に失敗しました。"])
            : AdminApiResult<SubjectNameNormalizationApplyResultDto>.Ok(value.Normalization);
    }
}
