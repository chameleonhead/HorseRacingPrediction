namespace HorseRacingPrediction.ApiClient;

public interface IApiClientFactory
{
    TApi Create<TApi>() where TApi : class;
}
