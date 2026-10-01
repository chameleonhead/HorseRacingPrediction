using Refit;
using HorseRacingPrediction.Contracts.MachineLearning;

namespace HorseRacingPrediction.ApiClient.MachineLearning;

internal interface IMachineLearningTransport
{
    [Get("/api/races/{raceId}/ml-prediction")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.MachineLearning.GetMlPredictionResponse>> GetMlPredictionAsync([AliasAs("raceId")] string raceId, CancellationToken cancellationToken);
    [Post("/api/ml/train")] Task<ApiResponse<global::HorseRacingPrediction.Contracts.MachineLearning.TrainMlModelResponse>> TrainMlModelAsync(CancellationToken cancellationToken);
}
