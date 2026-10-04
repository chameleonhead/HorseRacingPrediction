using HorseRacingPrediction.Contracts.Memos;
using Refit;

namespace HorseRacingPrediction.ApiClient.Memos;

public interface IMemosApi
{
    Task<IApiResponse> ChangeMemoSubjectsAsync(ChangeMemoSubjectsRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CreateMemoResponse>> CreateMemoAsync(CreateMemoRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> DeleteMemoAsync(DeleteMemoRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<GetMemosBySubjectResponse>> GetMemosBySubjectAsync(GetMemosBySubjectRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> UpdateMemoAsync(UpdateMemoRequest request, CancellationToken cancellationToken = default);
}
