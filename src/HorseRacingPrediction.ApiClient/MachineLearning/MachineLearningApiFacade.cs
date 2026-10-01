using Refit;

namespace HorseRacingPrediction.ApiClient.MachineLearning;

internal sealed class MachineLearningApiFacade(IMachineLearningTransport transport) : IMachineLearningApi
{
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.MachineLearning.GetMlPredictionResponse>> GetMlPredictionAsync(global::HorseRacingPrediction.Contracts.MachineLearning.GetMlPredictionRequest request, CancellationToken cancellationToken = default) => transport.GetMlPredictionAsync(request.RaceId, cancellationToken);
    public Task<ApiResponse<global::HorseRacingPrediction.Contracts.MachineLearning.TrainMlModelResponse>> TrainMlModelAsync(CancellationToken cancellationToken = default) => transport.TrainMlModelAsync(cancellationToken);
}
