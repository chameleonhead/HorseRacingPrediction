using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionApiV2RouteContractTests
{
    private static readonly Regex RouteCell = new(
        @"^\s*(?<method>GET|POST|PUT|PATCH|DELETE)\s+`(?<path>[^`]+)`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Parameter = new(@"\{[^}]+\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [TestMethod]
    public async Task RouteInventory_RegistersEveryCanonicalRouteAndRejectsEveryRetiredMethodPath()
    {
        var rows = ReadApprovedRouteRows();
        Assert.HasCount(69, rows);
        Assert.AreEqual(69, rows.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count(),
            "The approved route inventory must list each route ID exactly once.");

        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var lifetime = app;
        using var unauthenticated = app.GetTestClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await unauthenticated.GetAsync("/api/v2/admin/collection/tasks?limit=1")).StatusCode,
            "The new admin API surface must retain API-key protection.");
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);
        var mapped = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => new RouteIdentity(method, Normalize(endpoint.RoutePattern.RawText ?? string.Empty))))
            .ToArray();

        var canonicalRows = rows.Where(x => x.Canonical is not null).ToArray();
        var expectedRoutes = canonicalRows.Select(x => x.Canonical!.Value)
            .Select(x => new RouteIdentity(x.Method, Normalize(x.Path.Split('?')[0])))
            .Distinct().ToArray();
        Assert.AreEqual(59, expectedRoutes.Length,
            "The route contract has 59 distinct method/path registrations.");

        var scopedRegistrations = mapped.Where(x => IsMigrationScope(x.Path)).ToArray();
        var actualCounts = scopedRegistrations.GroupBy(x => x)
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (var route in expectedRoutes)
        {
            Assert.IsTrue(actualCounts.TryGetValue(route, out var count),
                $"Canonical route {route.Method} {route.Path} is not registered.");
            Assert.AreEqual(1, count,
                $"Canonical route {route.Method} {route.Path} must be registered exactly once.");
        }
        CollectionAssert.AreEquivalent(expectedRoutes, actualCounts.Keys.ToArray(),
            "The migration scope must contain exactly the canonical method/path set, without extra routes.");

        foreach (var row in rows)
        {
            var legacyIdentity = new RouteIdentity(row.Legacy.Method, Normalize(row.Legacy.Path));
            Assert.IsFalse(actualCounts.ContainsKey(legacyIdentity),
                $"Retired route {row.Id} remains registered: {row.Legacy.Method} {row.Legacy.Path}.");
            var path = Materialize(row.Legacy.Path);
            using var request = new HttpRequestMessage(new HttpMethod(row.Legacy.Method), path);
            if (row.Legacy.Method is "POST" or "PUT" or "PATCH")
                request.Content = new StringContent("{}");
            using var response = await client.SendAsync(request);
            Assert.IsTrue(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"Retired route {row.Id} returned {(int)response.StatusCode} for {row.Legacy.Method} {path}.");
        }

        await AssertGeneratedSwaggerInventoryAsync(app, client, expectedRoutes);
    }

    private static async Task AssertGeneratedSwaggerInventoryAsync(WebApplication app, HttpClient client,
        IReadOnlyCollection<RouteIdentity> expectedRoutes)
    {
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "TestApplicationFactory must expose the generated Swagger document.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var swaggerRoutes = new List<(RouteIdentity Route, string? OperationId)>();
        foreach (var path in paths.EnumerateObject())
        {
            var normalizedPath = Normalize(path.Name);
            if (!IsMigrationScope(normalizedPath)) continue;
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Name is not ("get" or "post" or "put" or "patch" or "delete")) continue;
                var operationId = operation.Value.TryGetProperty("operationId", out var id)
                    ? id.GetString()
                    : null;
                swaggerRoutes.Add((new(operation.Name, normalizedPath), operationId));
            }
        }

        var swaggerCounts = swaggerRoutes.GroupBy(x => x.Route)
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (var route in expectedRoutes)
        {
            Assert.IsTrue(swaggerCounts.TryGetValue(route, out var count),
                $"Swagger is missing canonical operation {route.Method} {route.Path}.");
            Assert.AreEqual(1, count,
                $"Swagger must expose canonical operation {route.Method} {route.Path} exactly once.");
        }
        CollectionAssert.AreEquivalent(expectedRoutes.ToArray(), swaggerCounts.Keys.ToArray(),
            "Swagger must expose exactly the canonical method/path set in the migration scope.");

        var operationIds = swaggerRoutes.Select(x => x.OperationId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        Assert.IsNotEmpty(operationIds,
            "The generated document must retain the named WithName operations.");
        Assert.AreEqual(operationIds.Length, operationIds.Distinct(StringComparer.Ordinal).Count(),
            "Generated operationIds must be unique within the migration scope.");

        var endpointNames = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => IsMigrationScope(Normalize(endpoint.RoutePattern.RawText ?? string.Empty)))
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
        Assert.IsNotEmpty(endpointNames,
            "The migrated route scope must retain explicit WithName metadata.");
        Assert.AreEqual(endpointNames.Length, endpointNames.Distinct(StringComparer.Ordinal).Count(),
            "WithName values must be unique within the migration scope.");
        CollectionAssert.AreEquivalent(endpointNames, operationIds,
            "Swagger operationIds must match the endpoint WithName values.");

        foreach (var row in ReadApprovedRouteRows())
        {
            var legacyPath = Normalize(row.Legacy.Path);
            if (paths.TryGetProperty(legacyPath, out var legacyOperations))
                Assert.IsFalse(legacyOperations.TryGetProperty(row.Legacy.Method.ToLowerInvariant(), out _),
                    $"Swagger must not advertise retired operation {row.Legacy.Method} {row.Legacy.Path}.");
        }
    }

    private static IReadOnlyList<LedgerRoute> ReadApprovedRouteRows()
    {
        var root = FindRepositoryRoot();
        var readme = File.ReadAllLines(Path.Combine(root, "docs", "changes", "20260927_collection-rest-api", "README.md"));
        var tableStart = Array.FindIndex(readme, line => line.StartsWith("| ID | Exact current method/path", StringComparison.Ordinal));
        Assert.IsGreaterThanOrEqualTo(0, tableStart, "The finite route inventory table is missing.");

        var routes = new List<LedgerRoute>();
        for (var index = tableStart + 2; index < readme.Length && readme[index].StartsWith('|'); index++)
        {
            var columns = readme[index].Split('|');
            if (columns.Length < 5) continue;
            var id = columns[1].Trim();
            var legacy = ParseRoute(columns[2]);
            if (legacy is null) continue;
            var canonical = ParseRoute(columns[4]);
            routes.Add(new(id, legacy.Value, canonical));
        }
        return routes;
    }

    private static RouteIdentity? ParseRoute(string value)
    {
        var match = RouteCell.Match(value);
        return match.Success
            ? new RouteIdentity(match.Groups["method"].Value, match.Groups["path"].Value)
            : null;
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
        => Parameter.Replace(path.TrimEnd('/'), "{}");

    private static bool IsMigrationScope(string path)
        => path.StartsWith("/api/v2/admin/collection", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/api/v2/admin/subjects", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/api/v2/admin/races", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/api/v2/internal/collection", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/api/v2/internal/prediction", StringComparison.OrdinalIgnoreCase);

    private static string Materialize(string path)
        => Regex.Replace(path, @"\{(?<name>[^}:]+)(?::[^}]+)?\}", match => match.Groups["name"].Value switch
        {
            "raceId" => "race-00000000-0000-0000-0000-000000000001",
            "notificationId" or "taskId" or "executionBatchId" => "00000000-0000-0000-0000-000000000001",
            "revision" or "page" or "pageSize" or "year" or "month" => "1",
            "type" => "Race",
            "provider" => "JRA",
            "resourceId" => "race-00000000-0000-0000-0000-000000000001",
            "definition" => "race-detail",
            "groupKey" => "unknown-group",
            _ => "contract-probe",
        });

    private sealed record LedgerRoute(string Id, RouteIdentity Legacy, RouteIdentity? Canonical);
    private readonly record struct RouteIdentity(string Method, string Path)
    {
        public bool Equals(RouteIdentity other)
            => string.Equals(Method, other.Method, StringComparison.OrdinalIgnoreCase)
               && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
        public override int GetHashCode()
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Method),
                StringComparer.OrdinalIgnoreCase.GetHashCode(Path));
    }
}
