using Refit;

namespace HorseRacingPrediction.ApiClient.Subjects;

internal sealed class SubjectsApiFacade(ISubjectsTransport transport) : ISubjectsApi
{
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.Subjects.GetSubjectProfileResponse>> GetSubjectProfileAsync(global::HorseRacingPrediction.Contracts.Subjects.GetSubjectProfileRequest request, CancellationToken cancellationToken = default) => transport.GetSubjectProfileAsync(request.Kind, request.SubjectId, cancellationToken);
    public Task<IApiResponse> PutSubjectProfileAsync(global::HorseRacingPrediction.Contracts.Subjects.PutSubjectProfileRequest request, CancellationToken cancellationToken = default) => transport.PutSubjectProfileAsync(request.Kind, request.SubjectId, request, cancellationToken);
}
