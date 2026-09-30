using HorseRacingPrediction.CollectionOperations.CollectionPlatform;

using HorseRacingPrediction.Contracts.Collection;
using HorseRacingPrediction.Contracts.Repairs;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

public sealed partial class AdminApiClient
{
    public Task<SubjectNameNormalizationPageDto?> SearchSubjectNameNormalizationAsync(
        CollectionResourceType subjectType, string query, int page, int pageSize = 25,
        CancellationToken token = default) => GetJsonAsync<SubjectNameNormalizationPageDto>(
            $"/api/admin/repairs/subject-name-normalization?subjectType={subjectType}&query={Uri.EscapeDataString(query)}&page={page}&pageSize={pageSize}",
            token);

    public Task<AdminApiResult<SubjectNameNormalizationApplyResultDto>> ApplySubjectNameNormalizationAsync(
        ApplySubjectNameNormalizationRequest request, CancellationToken token = default) =>
        SendCollectionPlatformAsync<SubjectNameNormalizationApplyResultDto>(HttpMethod.Post,
            "/api/admin/repairs/subject-name-normalization/apply", request, token);
}
