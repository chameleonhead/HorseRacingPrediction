using HorseRacingPrediction.ApiClient.Collection;
using HorseRacingPrediction.ApiClient.Horses;
using HorseRacingPrediction.ApiClient.Identity;
using HorseRacingPrediction.ApiClient.Jockeys;
using HorseRacingPrediction.ApiClient.MachineLearning;
using HorseRacingPrediction.ApiClient.Memos;
using HorseRacingPrediction.ApiClient.Owners;
using HorseRacingPrediction.ApiClient.Predictions;
using HorseRacingPrediction.ApiClient.PredictionScheduling;
using HorseRacingPrediction.ApiClient.Races;
using HorseRacingPrediction.ApiClient.Repairs;
using HorseRacingPrediction.ApiClient.Subjects;
using HorseRacingPrediction.ApiClient.Trainers;
using Refit;

namespace HorseRacingPrediction.ApiClient;

internal sealed class ApiClientFactory(IHttpClientFactory httpClientFactory) : IApiClientFactory
{
    internal const string HttpClientName = "HorseRacingPrediction.ApiClient";

    private static readonly RefitSettings Settings = new()
    {
        ContentSerializer = new SystemTextJsonContentSerializer(ApiClientJsonContext.CreateOptions()),
        UrlParameterFormatter = new ApiClientUrlParameterFormatter()
    };

    public TApi Create<TApi>() where TApi : class
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        object api = typeof(TApi) switch
        {
            var type when type == typeof(IRacesApi) => new RacesApiFacade(
                RestService.For<IRacesTransport>(client, Settings)),
            var type when type == typeof(IHorsesApi) => new HorsesApiFacade(
                RestService.For<IHorsesTransport>(client, Settings)),
            var type when type == typeof(IJockeysApi) => new JockeysApiFacade(
                RestService.For<IJockeysTransport>(client, Settings)),
            var type when type == typeof(ITrainersApi) => new TrainersApiFacade(
                RestService.For<ITrainersTransport>(client, Settings)),
            var type when type == typeof(IOwnersApi) => new OwnersApiFacade(
                RestService.For<IOwnersTransport>(client, Settings)),
            var type when type == typeof(IPredictionsApi) => new PredictionsApiFacade(
                RestService.For<IPredictionsTransport>(client, Settings)),
            var type when type == typeof(IMemosApi) => new MemosApiFacade(
                RestService.For<IMemosTransport>(client, Settings)),
            var type when type == typeof(IMachineLearningApi) => new MachineLearningApiFacade(
                RestService.For<IMachineLearningTransport>(client, Settings)),
            var type when type == typeof(IPredictionSchedulingApi) => new PredictionSchedulingApiFacade(
                RestService.For<IPredictionSchedulingTransport>(client, Settings)),
            var type when type == typeof(IRepairsApi) => new RepairsApiFacade(
                RestService.For<IRepairsTransport>(client, Settings)),
            var type when type == typeof(IIdentityApi) => new IdentityApiFacade(
                RestService.For<IIdentityTransport>(client, Settings)),
            var type when type == typeof(ISubjectsApi) => new SubjectsApiFacade(
                RestService.For<ISubjectsTransport>(client, Settings)),
            var type when type == typeof(ICollectionApi) => new CollectionApiFacade(
                RestService.For<ICollectionTransport>(client, Settings)),
            _ => throw new NotSupportedException($"API interface '{typeof(TApi).FullName}' is not registered.")
        };

        return (TApi)api;
    }
}
