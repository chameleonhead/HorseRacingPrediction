using HorseRacingPrediction.Contracts.MachineLearning;
using Refit;

namespace HorseRacingPrediction.ApiClient.MachineLearning;

public interface IMachineLearningApi
{
    Task<ApiResponse<GetMlPredictionResponse>> GetMlPredictionAsync(GetMlPredictionRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<TrainMlModelResponse>> TrainMlModelAsync(CancellationToken cancellationToken = default);
}
