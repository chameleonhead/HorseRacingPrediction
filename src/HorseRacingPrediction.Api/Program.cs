using EventFlow;
using EventFlow.EntityFramework.Extensions;
using EventFlow.Extensions;
using HorseRacingPrediction.Api;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.MachineLearning;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiKeyOptions>(options =>
{
    options.HeaderName = builder.Configuration["ApiKey:HeaderName"] ?? "X-Api-Key";
    var configuredKey = builder.Configuration["ApiKey:Key"];
    options.Key = string.IsNullOrWhiteSpace(configuredKey)
        ? Environment.GetEnvironmentVariable("HORSE_RACING_API_KEY")
        : configuredKey;
});

builder.Services.AddSingleton<ApiKeyEndpointFilter>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
    options.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace("+", "."));
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Description = "API キーをヘッダーに指定してください"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Data Source=eventstore.db";

builder.Services.AddSqliteDbContextProvider(connectionString);

builder.Services.AddSingleton<HorseWeightHistoryLocator>();
builder.Services.AddSingleton<PredictionComparisonViewLocator>();
builder.Services.AddSingleton<MemoBySubjectLocator>();
builder.Services.AddSingleton<HorseRaceHistoryLocator>();
builder.Services.AddSingleton<JockeyRaceHistoryLocator>();
builder.Services.AddRacePredictor();

builder.Services.AddEventFlow(options =>
{
    options
    .AddDefaults(typeof(RaceAggregate).Assembly)
    .AddDefaults(typeof(CreateRaceCommand).Assembly)
    .UseEntityFrameworkSqliteEventStore(connectionString)
    .UseEntityFrameworkReadModel<RaceSummaryReadModel, EventStoreDbContext>()
    .UseEntityFrameworkReadModel<HorseReadModel, EventStoreDbContext>()
    .UseEntityFrameworkReadModel<JockeyReadModel, EventStoreDbContext>()
    .UseEntityFrameworkReadModel<TrainerReadModel, EventStoreDbContext>()
    .UseEntityFrameworkReadModel<RacePredictionContextReadModel, EventStoreDbContext>()
    .UseEntityFrameworkReadModel<RaceResultViewReadModel, EventStoreDbContext>()
    .UseEntityFrameworkReadModel<PredictionTicketReadModel, EventStoreDbContext>()
    .UseEntityFrameworkReadModel<HorseWeightHistoryReadModel, EventStoreDbContext, HorseWeightHistoryLocator>()
    .UseEntityFrameworkReadModel<PredictionComparisonViewReadModel, EventStoreDbContext, PredictionComparisonViewLocator>()
    .UseEntityFrameworkReadModel<MemoBySubjectReadModel, EventStoreDbContext, MemoBySubjectLocator>()
    .UseEntityFrameworkReadModel<HorseRaceHistoryReadModel, EventStoreDbContext, HorseRaceHistoryLocator>()
    .UseEntityFrameworkReadModel<JockeyRaceHistoryReadModel, EventStoreDbContext, JockeyRaceHistoryLocator>();
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();

app.MapApiEndpoints();

app.Run();

