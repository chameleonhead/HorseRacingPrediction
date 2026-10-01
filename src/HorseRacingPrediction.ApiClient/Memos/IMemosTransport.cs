using Refit;
using HorseRacingPrediction.Contracts.Memos;

namespace HorseRacingPrediction.ApiClient.Memos;

internal interface IMemosTransport
{
    [Put("/api/memos/{memoId}/subjects")] Task<IApiResponse> ChangeMemoSubjectsAsync([AliasAs("memoId")] string memoId, [Body] global::HorseRacingPrediction.Contracts.Memos.ChangeMemoSubjectsRequest request, CancellationToken cancellationToken);
    [Post("/api/memos")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Memos.CreateMemoResponse>> CreateMemoAsync([Body] global::HorseRacingPrediction.Contracts.Memos.CreateMemoRequest request, CancellationToken cancellationToken);
    [Delete("/api/memos/{memoId}")] Task<IApiResponse> DeleteMemoAsync([AliasAs("memoId")] string memoId, CancellationToken cancellationToken);
    [Get("/api/memos/by-subject/{subjectType}/{subjectId}")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.Memos.GetMemosBySubjectResponse>> GetMemosBySubjectAsync([AliasAs("subjectType")] string subjectType, [AliasAs("subjectId")] string subjectId, CancellationToken cancellationToken);
    [Put("/api/memos/{memoId}")] Task<IApiResponse> UpdateMemoAsync([AliasAs("memoId")] string memoId, [Body] global::HorseRacingPrediction.Contracts.Memos.UpdateMemoRequest request, CancellationToken cancellationToken);
}
