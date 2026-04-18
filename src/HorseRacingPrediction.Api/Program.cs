using EventFlow;
using EventFlow.Extensions;
using HorseRacingPrediction.Api;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiKeyOptions>(options =>
{
    options.HeaderName = builder.Configuration["ApiKey:HeaderName"] ?? "X-Api-Key";
    options.Key = builder.Configuration["ApiKey:Key"]
        ?? Environment.GetEnvironmentVariable("HORSE_RACING_API_KEY");
});

builder.Services.AddSingleton<ApiKeyEndpointFilter>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
});

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Data Source=eventstore.db";

builder.Services.AddSqliteDbContextProvider(connectionString);

builder.Services.AddSingleton<HorseWeightHistoryLocator>();
builder.Services.AddSingleton<PredictionComparisonViewLocator>();

builder.Services.AddEventFlow(options =>
{
    options
    .AddDefaults(typeof(RaceAggregate).Assembly)
    .AddDefaults(typeof(CreateRaceCommand).Assembly)
    .UseEntityFrameworkSqliteEventStore(connectionString)
    .UseInMemoryReadStoreFor<HorseReadModel>()
    .UseInMemoryReadStoreFor<JockeyReadModel>()
    .UseInMemoryReadStoreFor<TrainerReadModel>()
    .UseInMemoryReadStoreFor<RacePredictionContextReadModel>()
    .UseInMemoryReadStoreFor<RaceResultViewReadModel>()
    .UseInMemoryReadStoreFor<PredictionTicketReadModel>()
    .UseInMemoryReadStoreFor<HorseWeightHistoryReadModel, HorseWeightHistoryLocator>()
    .UseInMemoryReadStoreFor<PredictionComparisonViewReadModel, PredictionComparisonViewLocator>();
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();

app.MapApiEndpoints();

app.Run();

