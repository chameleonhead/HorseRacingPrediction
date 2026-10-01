using System.Text.Json;
using System.Text.RegularExpressions;
using HorseRacingPrediction.Contracts.Collection;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionResponseMetadataTests
{
    private static readonly Regex RouteParameter = new(@"\{[^}:]+(?::[^}]+)?\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [TestMethod]
    public async Task CollectionResponseMetadata_MatchesFrozenOperationDtosAndSuccessStatuses()
    {
        var root = FindRepositoryRoot();
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "changes",
            "20260930_refit-api-clients", "http-contract-inventory.json")));
        var expected = inventory.RootElement.EnumerateArray()
            .Where(row => row.GetProperty("family").GetString() == "Collection")
            .Select(row => new
            {
                Method = row.GetProperty("method").GetString()!,
                Path = Normalize(row.GetProperty("path").GetString()!),
                File = row.GetProperty("endpointFile").GetString()!,
                Produces = row.GetProperty("produces").EnumerateArray()
                    .Select(item => (Type: item.GetProperty("type").GetString()!,
                        Status: item.GetProperty("status").GetInt32())).ToArray(),
            }).ToArray();
        Assert.HasCount(44, expected);
        Assert.AreEqual(40, expected.Count(row => row.Produces.Length > 0));

        var (app, _) = await TestApplicationFactory.CreateAsync();
        await using var lifetime = app;
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => new
                {
                    Method = method,
                    Path = Normalize(endpoint.RoutePattern.RawText ?? string.Empty),
                    Metadata = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
                        .Select(item => (item.Type, item.StatusCode)).ToArray(),
                }))
            .Where(endpoint => endpoint.Path.StartsWith("/api/v2/admin/collection", StringComparison.OrdinalIgnoreCase)
                || endpoint.Path.StartsWith("/api/v2/internal/collection", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var operation in expected)
        {
            var matches = endpoints.Where(endpoint => string.Equals(endpoint.Method, operation.Method,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(endpoint.Path, operation.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.HasCount(1, matches, $"Expected exactly one endpoint for {operation.Method} {operation.Path}.");
            var expectedTyped = operation.Produces.Select(item =>
            {
                var type = typeof(CreateCollectionTaskResponse).Assembly.GetType(
                    $"HorseRacingPrediction.Contracts.Collection.{item.Type}");
                Assert.IsNotNull(type, $"{operation.File} references missing contract type {item.Type}.");
                return (type, (int?)item.Status);
            }).ToArray();
            var expectedStatuses = expectedTyped.Select(item => item.Item2).ToHashSet();
            var actualTyped = matches[0].Metadata.Where(item => item.Type is not null
                    && expectedStatuses.Contains(item.StatusCode))
                .Select(item => (item.Type!, (int?)item.StatusCode)).Distinct().ToArray();
            CollectionAssert.AreEquivalent(expectedTyped, actualTyped,
                $"Typed success metadata differs from the frozen inventory for {operation.Method} {operation.Path}. "
                + $"Actual: {string.Join(", ", matches[0].Metadata.Select(item => $"{item.Type?.Name ?? "<none>"}:{item.StatusCode}"))}.");
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HorseRacingPrediction.sln"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the HorseRacingPrediction repository root.");
    }

    private static string Normalize(string path)
        => RouteParameter.Replace(path.TrimEnd('/'), "{}");
}
