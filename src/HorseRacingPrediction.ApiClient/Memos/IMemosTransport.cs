using HorseRacingPrediction.Contracts.Memos;
using Refit;

namespace HorseRacingPrediction.ApiClient.Memos;

internal interface IMemosTransport
{
    [Put("/api/memos/{memoId}/subjects")] Task<IApiResponse> ChangeMemoSubjectsAsync([AliasAs("memoId")] string memoId, [Body] ChangeMemoSubjectsRequest request, CancellationToken cancellationToken);
    [Post("/api/memos")] Task<ApiResponse<CreateMemoResponse>> CreateMemoAsync([Body] CreateMemoRequest request, CancellationToken cancellationToken);
    [Delete("/api/memos/{memoId}")] Task<IApiResponse> DeleteMemoAsync([AliasAs("memoId")] string memoId, CancellationToken cancellationToken);
    [Get("/api/memos/by-subject/{subjectType}/{subjectId}")] Task<ApiResponse<GetMemosBySubjectResponse>> GetMemosBySubjectAsync([AliasAs("subjectType")] string subjectType, [AliasAs("subjectId")] string subjectId, CancellationToken cancellationToken);
    [Put("/api/memos/{memoId}")] Task<IApiResponse> UpdateMemoAsync([AliasAs("memoId")] string memoId, [Body] UpdateMemoRequest request, CancellationToken cancellationToken);
}
