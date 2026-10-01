using Refit;

namespace HorseRacingPrediction.ApiClient.Subjects;

public interface ISubjectsApi
{
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.Subjects.GetSubjectProfileResponse>> GetSubjectProfileAsync(global::HorseRacingPrediction.Contracts.Subjects.GetSubjectProfileRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> PutSubjectProfileAsync(global::HorseRacingPrediction.Contracts.Subjects.PutSubjectProfileRequest request, CancellationToken cancellationToken = default);
}
