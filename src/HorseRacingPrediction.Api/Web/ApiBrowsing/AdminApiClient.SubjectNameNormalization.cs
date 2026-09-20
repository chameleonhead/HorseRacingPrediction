using HorseRacingPrediction.Api.Contracts;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    public Task<SubjectNameNormalizationPage?> SearchSubjectNameNormalizationAsync(
        ResourceType subjectType, string query, int page, int pageSize = 25,
        CancellationToken token = default) => GetJsonAsync<SubjectNameNormalizationPage>(
            $"/api/admin/repairs/subject-name-normalization?subjectType={subjectType}&query={Uri.EscapeDataString(query)}&page={page}&pageSize={pageSize}",
            token);

    public Task<AdminApiResult<SubjectNameNormalizationApplyResult>> ApplySubjectNameNormalizationAsync(
        ApplySubjectNameNormalizationRequest request, CancellationToken token = default) =>
        SendCollectionPlatformAsync<SubjectNameNormalizationApplyResult>(HttpMethod.Post,
            "/api/admin/repairs/subject-name-normalization/apply", request, token);
}
