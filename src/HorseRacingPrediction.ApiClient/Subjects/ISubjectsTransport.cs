using HorseRacingPrediction.Contracts.Subjects;
using Refit;

namespace HorseRacingPrediction.ApiClient.Subjects;

internal interface ISubjectsTransport
{
    [Get("/api/v2/admin/subjects/{kind}/{subjectId}/profiles/current")] Task<ApiResponse<GetSubjectProfileResponse>> GetSubjectProfileAsync([AliasAs("kind")] string kind, [AliasAs("subjectId")] string subjectId, CancellationToken cancellationToken);
    [Put("/api/v2/admin/subjects/{kind}/{subjectId}/profile")] Task<IApiResponse> PutSubjectProfileAsync([AliasAs("kind")] string kind, [AliasAs("subjectId")] string subjectId, [Body] PutSubjectProfileRequest request, CancellationToken cancellationToken);
}
