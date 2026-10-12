using System.Collections.Concurrent;
using System.Net;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Api.Web.ApiBrowsing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class AdminApiClientLoggingTests
{
    [TestMethod]
    public async Task DefaultTypedClientLoggingCapturesPathMarker_AndRemovedLoggersCaptureNothing()
    {
        var defaultLogging = await SendAsync(removeAllLoggers: false);
        Assert.IsTrue(defaultLogging.Messages.Any(message =>
            message.Contains(DefaultHostMarker, StringComparison.Ordinal)));

        var safeLogging = await SendAsync(removeAllLoggers: true);
        Assert.IsFalse(safeLogging.Messages.Any(message =>
            message.Contains(DefaultHostMarker, StringComparison.Ordinal)));
        Assert.IsFalse(safeLogging.Messages.Any(message =>
            message.Contains(CursorMarker, StringComparison.Ordinal)));
        Assert.IsTrue(safeLogging.RequestUri!.Query.Contains(CursorMarker, StringComparison.Ordinal));
    }

    private const string DefaultHostMarker = "synthetic-log-capture-marker.example.test";
    private const string CursorMarker = "synthetic-protected-cursor-marker";

    private static async Task<CapturedLogging> SendAsync(bool removeAllLoggers)
    {
        var loggerProvider = new CapturingLoggerProvider();
        var handler = new CapturingHandler();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(loggerProvider);
        });
        services.AddSingleton<AdminApiBaseAddressResolver>(_ =>
            new AdminApiBaseAddressResolver(new StubServer()));

        var clientBuilder = services.AddHttpClient<AdminApiClient>(client =>
            client.BaseAddress = new Uri($"https://{DefaultHostMarker}/"));
        clientBuilder.ConfigurePrimaryHttpMessageHandler(() => handler);
        if (removeAllLoggers)
            clientBuilder.RemoveAllLoggers();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<AdminApiClient>();
        var result = await client.GetIdentityMigrationPreflightAsync(CursorMarker);

        Assert.IsNull(result);
        Assert.IsNotNull(handler.RequestUri);
        return new CapturedLogging(handler.RequestUri, loggerProvider.Messages.ToArray());
    }

    private sealed record CapturedLogging(Uri RequestUri, IReadOnlyList<string> Messages);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => messages.Enqueue(formatter(state, exception));
    }

    private sealed class StubServer : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application,
            CancellationToken cancellationToken) where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
