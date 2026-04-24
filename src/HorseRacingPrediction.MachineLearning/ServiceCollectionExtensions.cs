using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML;

namespace HorseRacingPrediction.MachineLearning;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// ML.NET の <see cref="IRacePredictor"/> をシングルトンとして登録する。
    /// </summary>
    public static IServiceCollection AddRacePredictor(this IServiceCollection services)
    {
        services.AddSingleton<MLContext>(_ => new MLContext(seed: 42));
        services.AddSingleton<IRacePredictor, RacePredictor>();
        return services;
    }
}
