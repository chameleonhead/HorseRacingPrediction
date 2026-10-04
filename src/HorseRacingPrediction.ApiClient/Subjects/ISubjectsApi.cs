using HorseRacingPrediction.Contracts.Subjects;
using Refit;

namespace HorseRacingPrediction.ApiClient.Subjects;

public interface ISubjectsApi
{
    Task<ApiResponse<GetSubjectProfileResponse>> GetSubjectProfileAsync(GetSubjectProfileRequest request, CancellationToken cancellationToken = default);
    Task<IApiResponse> PutSubjectProfileAsync(PutSubjectProfileRequest request, CancellationToken cancellationToken = default);
}
