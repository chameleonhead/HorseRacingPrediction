using HorseRacingPrediction.Contracts.Memos;
using Refit;

namespace HorseRacingPrediction.ApiClient.Memos;

internal sealed class MemosApiFacade(IMemosTransport transport) : IMemosApi
{
    public Task<IApiResponse> ChangeMemoSubjectsAsync(ChangeMemoSubjectsRequest request, CancellationToken cancellationToken = default) => transport.ChangeMemoSubjectsAsync(request.MemoId, request, cancellationToken);
    public Task<ApiResponse<CreateMemoResponse>> CreateMemoAsync(CreateMemoRequest request, CancellationToken cancellationToken = default) => transport.CreateMemoAsync(request, cancellationToken);
    public Task<IApiResponse> DeleteMemoAsync(DeleteMemoRequest request, CancellationToken cancellationToken = default) => transport.DeleteMemoAsync(request.MemoId, cancellationToken);
    public Task<ApiResponse<GetMemosBySubjectResponse>> GetMemosBySubjectAsync(GetMemosBySubjectRequest request, CancellationToken cancellationToken = default) => transport.GetMemosBySubjectAsync(request.SubjectType, request.SubjectId, cancellationToken);
    public Task<IApiResponse> UpdateMemoAsync(UpdateMemoRequest request, CancellationToken cancellationToken = default) => transport.UpdateMemoAsync(request.MemoId, request, cancellationToken);
}
