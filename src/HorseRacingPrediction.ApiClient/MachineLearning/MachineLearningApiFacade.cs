using HorseRacingPrediction.Contracts.MachineLearning;
using Refit;

namespace HorseRacingPrediction.ApiClient.MachineLearning;

internal sealed class MachineLearningApiFacade(IMachineLearningTransport transport) : IMachineLearningApi
{
    public Task<ApiResponse<GetMlPredictionResponse>> GetMlPredictionAsync(GetMlPredictionRequest request, CancellationToken cancellationToken = default) => transport.GetMlPredictionAsync(request.RaceId, cancellationToken);
    public Task<ApiResponse<TrainMlModelResponse>> TrainMlModelAsync(CancellationToken cancellationToken = default) => transport.TrainMlModelAsync(cancellationToken);
}
