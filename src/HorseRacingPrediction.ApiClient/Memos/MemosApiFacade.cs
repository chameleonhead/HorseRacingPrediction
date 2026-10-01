using Refit;

namespace HorseRacingPrediction.ApiClient.Memos;

internal sealed class MemosApiFacade(IMemosTransport transport) : IMemosApi
{
    public Task<IApiResponse> ChangeMemoSubjectsAsync(global::HorseRacingPrediction.Contracts.Memos.ChangeMemoSubjectsRequest request, CancellationToken cancellationToken = default) => transport.ChangeMemoSubjectsAsync(request.MemoId, request, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Memos.CreateMemoResponse>> CreateMemoAsync(global::HorseRacingPrediction.Contracts.Memos.CreateMemoRequest request, CancellationToken cancellationToken = default) => transport.CreateMemoAsync(request, cancellationToken);
    public Task<IApiResponse> DeleteMemoAsync(global::HorseRacingPrediction.Contracts.Memos.DeleteMemoRequest request, CancellationToken cancellationToken = default) => transport.DeleteMemoAsync(request.MemoId, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Memos.GetMemosBySubjectResponse>> GetMemosBySubjectAsync(global::HorseRacingPrediction.Contracts.Memos.GetMemosBySubjectRequest request, CancellationToken cancellationToken = default) => transport.GetMemosBySubjectAsync(request.SubjectType, request.SubjectId, cancellationToken);
    public Task<IApiResponse> UpdateMemoAsync(global::HorseRacingPrediction.Contracts.Memos.UpdateMemoRequest request, CancellationToken cancellationToken = default) => transport.UpdateMemoAsync(request.MemoId, request, cancellationToken);
}
