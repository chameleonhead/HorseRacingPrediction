using EventFlow;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.Extensions;
using EventFlow.Extensions;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

internal static class TestApplicationFactory
{
    public const string TestApiKey = "test-api-key-12345";

    public static async Task<(WebApplication App, HttpClient Client)> CreateAsync()
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();

        builder.Services.Configure<ApiKeyOptions>(opts =>
        {
            opts.HeaderName = "X-Api-Key";
            opts.Key = TestApiKey;
        });
        builder.Services.AddSingleton<ApiKeyEndpointFilter>();

        var dbContextProvider = new SqliteDbContextProvider("DataSource=:memory:");
        builder.Services.AddSingleton(dbContextProvider);
        builder.Services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(dbContextProvider);

        builder.Services.AddEventFlow(options =>
        {
            options
                .ConfigureEntityFramework(EntityFrameworkConfiguration.New)
                .AddDefaults(typeof(RaceAggregate).Assembly)
                .UseEntityFrameworkEventStore<EventStoreDbContext>();
        });

        var app = builder.Build();
        app.MapApiEndpoints();

        await app.StartAsync();
        var client = app.GetTestClient();
        return (app, client);
    }
}
