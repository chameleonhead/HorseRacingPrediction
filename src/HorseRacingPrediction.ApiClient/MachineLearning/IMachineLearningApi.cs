using Refit;

namespace HorseRacingPrediction.ApiClient.MachineLearning;

public interface IMachineLearningApi
{
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.MachineLearning.GetMlPredictionResponse>> GetMlPredictionAsync(global::HorseRacingPrediction.Contracts.MachineLearning.GetMlPredictionRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<global::HorseRacingPrediction.Contracts.MachineLearning.TrainMlModelResponse>> TrainMlModelAsync(CancellationToken cancellationToken = default);
}
