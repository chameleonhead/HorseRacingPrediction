using System.Net;
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
            .ToHashSet();

        var canonicalRows = rows.Where(x => x.Canonical is not null).ToArray();
        foreach (var row in canonicalRows)
        {
            var expected = row.Canonical!.Value;
            Assert.IsTrue(mapped.Contains(new RouteIdentity(expected.Method, Normalize(expected.Path.Split('?')[0]))),
                $"{row.Id} canonical route {expected.Method} {expected.Path} is not registered.");
        }

        foreach (var row in rows)
        {
            Assert.IsFalse(mapped.Contains(new RouteIdentity(row.Legacy.Method, Normalize(row.Legacy.Path))),
                $"Retired route {row.Id} remains registered: {row.Legacy.Method} {row.Legacy.Path}.");
            var path = Materialize(row.Legacy.Path);
            using var request = new HttpRequestMessage(new HttpMethod(row.Legacy.Method), path);
            if (row.Legacy.Method is "POST" or "PUT" or "PATCH")
                request.Content = new StringContent("{}");
            using var response = await client.SendAsync(request);
            Assert.IsTrue(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"Retired route {row.Id} returned {(int)response.StatusCode} for {row.Legacy.Method} {path}.");
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
