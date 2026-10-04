using HorseRacingPrediction.Contracts.Subjects;
using Refit;

namespace HorseRacingPrediction.ApiClient.Subjects;

internal sealed class SubjectsApiFacade(ISubjectsTransport transport) : ISubjectsApi
{
    public Task<ApiResponse<GetSubjectProfileResponse>> GetSubjectProfileAsync(GetSubjectProfileRequest request, CancellationToken cancellationToken = default) => transport.GetSubjectProfileAsync(request.Kind, request.SubjectId, cancellationToken);
    public Task<IApiResponse> PutSubjectProfileAsync(PutSubjectProfileRequest request, CancellationToken cancellationToken = default) => transport.PutSubjectProfileAsync(request.Kind, request.SubjectId, request, cancellationToken);
}
