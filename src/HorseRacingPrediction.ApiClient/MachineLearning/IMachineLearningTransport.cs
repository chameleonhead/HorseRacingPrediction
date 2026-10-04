using HorseRacingPrediction.Contracts.MachineLearning;
using Refit;

namespace HorseRacingPrediction.ApiClient.MachineLearning;

internal interface IMachineLearningTransport
{
    [Get("/api/races/{raceId}/ml-prediction")] Task<ApiResponse<GetMlPredictionResponse>> GetMlPredictionAsync([AliasAs("raceId")] string raceId, CancellationToken cancellationToken);
    [Post("/api/ml/train")] Task<ApiResponse<TrainMlModelResponse>> TrainMlModelAsync(CancellationToken cancellationToken);
}
