using HorseRacingPrediction.ApiClient;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using System.Net;

namespace HorseRacingPrediction.ApiClient.Tests;

[TestClass]
public sealed class ApiClientFactoryTests
{
    [TestMethod]
    public void Factory_ResolvesAllRegisteredApiInterfaces()
    {
        using var services = CreateServices(new ApiClientTestRecordingHandler());
        var factory = services.GetRequiredService<IApiClientFactory>();

        Assert.IsNotNull(factory.Create<IRacesApi>());
        Assert.IsNotNull(factory.Create<IHorsesApi>());
        Assert.IsNotNull(factory.Create<IJockeysApi>());
        Assert.IsNotNull(factory.Create<ITrainersApi>());
        Assert.IsNotNull(factory.Create<IOwnersApi>());
        Assert.IsNotNull(factory.Create<IPredictionsApi>());
        Assert.IsNotNull(factory.Create<IMemosApi>());
        Assert.IsNotNull(factory.Create<IMachineLearningApi>());
        Assert.IsNotNull(factory.Create<IPredictionSchedulingApi>());
        Assert.IsNotNull(factory.Create<IRepairsApi>());
        Assert.IsNotNull(factory.Create<IIdentityApi>());
        Assert.IsNotNull(factory.Create<ISubjectsApi>());
        Assert.IsNotNull(factory.Create<ICollectionApi>());
    }

    [TestMethod]
    public void Factory_RejectsUnregisteredInterface()
    {
        using var services = CreateServices(new ApiClientTestRecordingHandler());
        var factory = services.GetRequiredService<IApiClientFactory>();

        Assert.ThrowsExactly<NotSupportedException>(() => factory.Create<IApiClientTestUnregisteredApi>());
    }

    [TestMethod]
    public async Task ReturnedHttpClientBuilder_CustomizesHandlerAndAppliesDefaultApiKeyHeader()
    {
        var primary = new ApiClientTestRecordingHandler();
        var services = CreateServices(primary, "key-default");
        using (services)
        {
            var factory = services.GetRequiredService<IApiClientFactory>();
            using var response = await factory.Create<ICollectionApi>().GetCollectionPipelineAsync();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(response.Content?.Pipeline);
            var request = primary.Requests.Single();
            Assert.AreEqual("https://api.example.test/api/v2/admin/collection/pipeline-state",
                request.RequestUri!.AbsoluteUri);
            Assert.AreEqual("key-default", request.Headers.GetValues("X-Api-Key").Single());
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.IsNull(primary.Bodies.Single());
            Assert.AreEqual("configured", request.Headers.GetValues("X-Test-Handler").Single());
        }
    }

    [TestMethod]
    public async Task Factory_UsesCustomApiKeyHeaderWithoutAddingDefaultHeader()
    {
        var primary = new ApiClientTestRecordingHandler();
        var services = CreateServices(primary, "key-custom", "X-Client-Key");
        using (services)
        {
            var factory = services.GetRequiredService<IApiClientFactory>();
            using var response = await factory.Create<ICollectionApi>().GetCollectionPipelineAsync();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var request = primary.Requests.Single();
            Assert.AreEqual("key-custom", request.Headers.GetValues("X-Client-Key").Single());
            Assert.IsFalse(request.Headers.Contains("X-Api-Key"));
        }
    }

    [TestMethod]
    public async Task Factory_OmitsApiKeyHeaderWhenNoKeyIsConfigured()
    {
        var primary = new ApiClientTestRecordingHandler();
        using var services = CreateServices(primary, apiKey: null);
        using var response = await services.GetRequiredService<IApiClientFactory>()
            .Create<ICollectionApi>().GetCollectionPipelineAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(primary.Requests.Single().Headers.Contains("X-Api-Key"));
    }

    [TestMethod]
    public async Task Factory_ConcurrentFacadesDoNotSharePerRequestHeaderState()
    {
        var primary = new ApiClientTestRecordingHandler();
        using var services = CreateServices(primary, "stable-key");
        var factory = services.GetRequiredService<IApiClientFactory>();
        var clients = Enumerable.Range(0, 12).Select(_ => factory.Create<ICollectionApi>()).ToArray();

        var responses = await Task.WhenAll(clients.Select(client => client.GetCollectionPipelineAsync()));
        foreach (var response in responses)
            response.Dispose();

        Assert.HasCount(clients.Length, primary.Requests);
        Assert.IsTrue(primary.Requests.All(request =>
            request.Headers.GetValues("X-Api-Key").Single() == "stable-key"));
        Assert.IsTrue(primary.Requests.All(request => request.RequestUri!.Host == "api.example.test"));
    }

    [TestMethod]
    public void Registration_RejectsInvalidBaseAddressHeaderAndTimeout()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AddClient(new ApiClientOptions { ApiKey = null }));
        Assert.ThrowsExactly<ArgumentException>(() => AddClient(new ApiClientOptions
        {
            BaseAddress = new Uri("ftp://api.example.test/"),
            ApiKey = null
        }));
        Assert.ThrowsExactly<ArgumentException>(() => AddClient(new ApiClientOptions
        {
            BaseAddress = new Uri("/relative", UriKind.Relative),
            ApiKey = null
        }));
        Assert.ThrowsExactly<ArgumentException>(() => AddClient(new ApiClientOptions
        {
            BaseAddress = new Uri("https://api.example.test/"),
            ApiKey = null,
            ApiKeyHeaderName = "invalid header"
        }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AddClient(new ApiClientOptions
        {
            BaseAddress = new Uri("https://api.example.test/"),
            ApiKey = null,
            Timeout = TimeSpan.Zero
        }));

        using var acceptsInfiniteTimeout = AddClient(new ApiClientOptions
        {
            BaseAddress = new Uri("http://api.example.test/"),
            ApiKey = null,
            Timeout = Timeout.InfiniteTimeSpan
        });
        Assert.IsNotNull(acceptsInfiniteTimeout.GetRequiredService<IApiClientFactory>());
    }

    [TestMethod]
    public async Task Factory_TimeoutCancelsAnUnresponsiveTransport()
    {
        var handler = new ApiClientTestRecordingHandler(delayUntilCanceled: true);
        var services = new ServiceCollection();
        var builder = services.AddHorseRacingApiClient(options =>
        {
            options.BaseAddress = new Uri("https://api.example.test/");
            options.Timeout = TimeSpan.FromMilliseconds(200);
        });
        builder.ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        using var configuredClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(builder.Name);
        Assert.AreEqual(TimeSpan.FromMilliseconds(200), configuredClient.Timeout);
        using var response = await provider.GetRequiredService<IApiClientFactory>()
            .Create<ICollectionApi>().GetCollectionPipelineAsync();
        Assert.IsNotNull(response.Error);
        Assert.IsTrue(handler.ObservedCancellation,
            $"The configured HttpClient timeout returned {response.Error.GetType().Name} without cancelling the transport.");
    }

    private static ServiceProvider CreateServices(ApiClientTestRecordingHandler handler, string? apiKey = "test-key",
        string apiKeyHeaderName = "X-Api-Key")
    {
        var services = new ServiceCollection();
        var builder = services.AddHorseRacingApiClient(options =>
        {
            options.BaseAddress = new Uri("https://api.example.test/");
            options.ApiKey = apiKey;
            options.ApiKeyHeaderName = apiKeyHeaderName;
            options.Timeout = Timeout.InfiniteTimeSpan;
        });
        builder.AddHttpMessageHandler(() => new ApiClientTestMarkerHandler());
        builder.ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider AddClient(ApiClientOptions options)
    {
        var services = new ServiceCollection();
        services.AddHorseRacingApiClient(configured =>
        {
            configured.BaseAddress = options.BaseAddress;
            configured.ApiKey = options.ApiKey;
            configured.ApiKeyHeaderName = options.ApiKeyHeaderName;
            configured.Timeout = options.Timeout;
        });
        return services.BuildServiceProvider();
    }

}
