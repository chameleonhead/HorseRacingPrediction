using Refit;

namespace HorseRacingPrediction.ApiClient.Memos;

public interface IMemosApi
{
    Task<IApiResponse> ChangeMemoSubjectsAsync(global::HorseRacingPrediction.Contracts.Memos.ChangeMemoSubjectsRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Memos.CreateMemoResponse>> CreateMemoAsync(global::HorseRacingPrediction.Contracts.Memos.CreateMemoRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> DeleteMemoAsync(global::HorseRacingPrediction.Contracts.Memos.DeleteMemoRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Memos.GetMemosBySubjectResponse>> GetMemosBySubjectAsync(global::HorseRacingPrediction.Contracts.Memos.GetMemosBySubjectRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> UpdateMemoAsync(global::HorseRacingPrediction.Contracts.Memos.UpdateMemoRequest request, CancellationToken cancellationToken = default);
}
