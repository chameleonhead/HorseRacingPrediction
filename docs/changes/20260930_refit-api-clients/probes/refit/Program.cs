using System.Net;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Refit;

var handler = new ProbeHandler();
using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    TypeInfoResolver = ProbeJsonContext.Default
};
jsonOptions.Converters.Add(new NumericWriteStringOrNumberReadEnumConverter());
var settings = new RefitSettings(new SystemTextJsonContentSerializer(jsonOptions));
var transport = RestService.For<IRacesTransport>(http, settings);
try
{
    await RestService.For<IRf015DirectBodyTransport>(http, settings)
        .CompleteAsync(new CompleteRequest("job-1"), CancellationToken.None);
    throw new InvalidOperationException("Expected direct body-to-route binding failure");
}
catch (ArgumentException exception)
{
    Console.WriteLine("PASS RF015 ruled-out direct body route binding: " + exception.GetType().Name);
}
IRacesApi api = new RacesApiFacade(transport);

var get = await api.GetRaceAsync(new GetRaceRequest(
    "race-1", new DateOnly(2026, 5, 1),
    new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.FromHours(9)),
    QueryStatus.ResultDeclared, null));
Assert(get.IsSuccessStatusCode, "GET should succeed");
Assert(handler.LastRequest!.Method == HttpMethod.Get, "GET method");
Assert(handler.LastRequest.Content is null, "GET has no body");
Assert(handler.LastRequest.RequestUri!.AbsolutePath == "/api/races/race-1", "path id appears only in path");
var query = handler.LastRequest.RequestUri.Query;
Assert(query.Contains("raceDate=2026-05-01", StringComparison.Ordinal), "DateOnly ISO query");
Assert(query.Contains("modifiedAfter=2026-05-01T09%3A00%3A00.0000000%2B09%3A00", StringComparison.OrdinalIgnoreCase), "DateTimeOffset ISO query preserves offset");
Assert(query.Contains("status=1", StringComparison.Ordinal), "enum query uses its numeric contract value");
Assert(!query.Contains("search=", StringComparison.Ordinal), "null query omitted");
Assert(!query.Contains("raceId=", StringComparison.Ordinal), "path id not duplicated in query");
Assert(get.Content?.Race.RaceId == "race-1", "wrapped response content");
var pathQueryUri = handler.LastRequest.RequestUri;
var stringEnum = await api.GetStringEnumAsync(CancellationToken.None);
var numericEnum = await api.GetNumericEnumAsync(CancellationToken.None);
Assert(stringEnum.Content?.Status == QueryStatus.ResultDeclared, "string enum response deserializes");
Assert(numericEnum.Content?.Status == QueryStatus.ResultDeclared, "numeric enum response deserializes");
Console.WriteLine("PASS path/query, ISO date/time, numeric enum request, and string/numeric enum responses: " + pathQueryUri);

var create = await api.CreateRaceAsync(new CreateRaceRequest(
    new RaceDto("race-2", "Tokyo", new DateOnly(2026, 5, 2), QueryStatus.ResultDeclared)));
Assert(create.IsSuccessStatusCode, "wrapped POST should succeed");
Assert(handler.LastRequest!.Method == HttpMethod.Post, "POST method");
Assert(handler.LastRequest.RequestUri!.AbsolutePath == "/api/races", "POST route");
using (var body = JsonDocument.Parse(handler.LastBody!))
{
    Assert(body.RootElement.TryGetProperty("race", out var race), "request wraps RaceDto in race");
    Assert(race.GetProperty("raceId").GetString() == "race-2", "wrapped request data");
    Assert(race.GetProperty("status").GetInt32() == 1, "body enum request serializes numerically");
}
Assert(create.Content?.CreatedId == "race-2", "typed response data");
Console.WriteLine("PASS wrapped body/response");

var status = await api.GetStatusAsync(CancellationToken.None);
Assert(status.IsSuccessStatusCode, "input-free request succeeds");
Assert(handler.LastRequest!.RequestUri!.AbsolutePath == "/api/status", "input-free route");
Assert(handler.LastRequest.Content is null, "input-free GET has no body");
Console.WriteLine("PASS no input / no request object");

var noContent = await api.CompleteAsync(new CompleteRequest("job-1"), CancellationToken.None);
Assert(noContent.StatusCode == HttpStatusCode.NoContent, "204 remains no-content");
Assert(handler.LastRequest!.RequestUri!.AbsolutePath == "/api/jobs/job-1/complete", "204 route");
Console.WriteLine("PASS no response / 204");

var notFound = await api.GetMissingAsync(new GetMissingRequest("gone"), CancellationToken.None);
Assert(notFound.StatusCode == HttpStatusCode.NotFound, "404 status retained");
Assert(notFound.Error is ApiException { StatusCode: HttpStatusCode.NotFound }, "404 represented as Refit error");
Assert(notFound.Content is null, "404 is not successful content");
var conflict = await api.ConflictAsync(new ConflictRequest("race-3"), CancellationToken.None);
Assert(conflict.StatusCode == HttpStatusCode.Conflict, "409 status retained");
Assert(conflict.Error is ApiException { StatusCode: HttpStatusCode.Conflict }, "409 represented as Refit error");
Assert(conflict.Content is null, "409 is not successful content");
Console.WriteLine("PASS 404 / 409 error status");

using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
{
    try
    {
        await api.WaitAsync(new WaitRequest("wait-1"), cancellation.Token);
        throw new InvalidOperationException("Expected cancellation to propagate");
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        Console.WriteLine("PASS cancellation propagation");
    }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("ASSERT: " + message);
}

public sealed record GetRaceRequest(string RaceId, DateOnly? RaceDate, DateTimeOffset? ModifiedAfter, QueryStatus? Status, string? Search);
public sealed record CreateRaceRequest(RaceDto Race);
public sealed record RaceDto(string RaceId, string RacecourseCode, DateOnly RaceDate, QueryStatus Status);
public sealed record CreateRaceResponse(string CreatedId);
public sealed record GetRaceResponse(RaceDto Race);
public sealed record GetMissingRequest(string Id);
public sealed record ConflictRequest(string RaceId);
public sealed record CompleteRequest(string JobId);
public sealed record WaitRequest(string Id);
public sealed record GetRaceQuery(
    [property: AliasAs("raceDate")] string? RaceDate,
    [property: AliasAs("modifiedAfter")] string? ModifiedAfter,
    [property: AliasAs("status")] int? Status,
    [property: AliasAs("search")] string? Search);
public enum QueryStatus { Scheduled, ResultDeclared }

public interface IRacesApi
{
    Task<ApiResponse<GetRaceResponse>> GetRaceAsync(GetRaceRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<CreateRaceResponse>> CreateRaceAsync(CreateRaceRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StatusResponse>> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<IApiResponse> CompleteAsync(CompleteRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<MissingResponse>> GetMissingAsync(GetMissingRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<ConflictResponse>> ConflictAsync(ConflictRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<StatusResponse>> WaitAsync(WaitRequest request, CancellationToken cancellationToken = default);
    Task<ApiResponse<EnumResponse>> GetStringEnumAsync(CancellationToken cancellationToken = default);
    Task<ApiResponse<EnumResponse>> GetNumericEnumAsync(CancellationToken cancellationToken = default);
}

internal sealed class RacesApiFacade(IRacesTransport transport) : IRacesApi
{
    public Task<ApiResponse<GetRaceResponse>> GetRaceAsync(GetRaceRequest request, CancellationToken cancellationToken = default)
        => transport.GetRaceAsync(request.RaceId, new GetRaceQuery(
            request.RaceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            request.ModifiedAfter?.ToString("O", CultureInfo.InvariantCulture),
            request.Status is { } status ? (int)status : null,
            request.Search), cancellationToken);

    public Task<ApiResponse<CreateRaceResponse>> CreateRaceAsync(CreateRaceRequest request, CancellationToken cancellationToken = default)
        => transport.CreateRaceAsync(request, cancellationToken);

    public Task<ApiResponse<StatusResponse>> GetStatusAsync(CancellationToken cancellationToken = default)
        => transport.GetStatusAsync(cancellationToken);

    public Task<IApiResponse> CompleteAsync(CompleteRequest request, CancellationToken cancellationToken = default)
        => transport.CompleteAsync(request.JobId, cancellationToken);

    public Task<ApiResponse<MissingResponse>> GetMissingAsync(GetMissingRequest request, CancellationToken cancellationToken = default)
        => transport.GetMissingAsync(request.Id, cancellationToken);

    public Task<ApiResponse<ConflictResponse>> ConflictAsync(ConflictRequest request, CancellationToken cancellationToken = default)
        => transport.ConflictAsync(request.RaceId, cancellationToken);

    public Task<ApiResponse<StatusResponse>> WaitAsync(WaitRequest request, CancellationToken cancellationToken = default)
        => transport.WaitAsync(request.Id, cancellationToken);

    public Task<ApiResponse<EnumResponse>> GetStringEnumAsync(CancellationToken cancellationToken = default)
        => transport.GetEnumAsync("string", cancellationToken);

    public Task<ApiResponse<EnumResponse>> GetNumericEnumAsync(CancellationToken cancellationToken = default)
        => transport.GetEnumAsync("numeric", cancellationToken);
}

internal interface IRacesTransport
{
    [Get("/api/races/{raceId}")]
    Task<ApiResponse<GetRaceResponse>> GetRaceAsync(string raceId, [Query] GetRaceQuery query, CancellationToken cancellationToken);

    [Post("/api/races")]
    Task<ApiResponse<CreateRaceResponse>> CreateRaceAsync([Body] CreateRaceRequest request, CancellationToken cancellationToken);

    [Get("/api/status")]
    Task<ApiResponse<StatusResponse>> GetStatusAsync(CancellationToken cancellationToken);

    [Post("/api/jobs/{jobId}/complete")]
    Task<IApiResponse> CompleteAsync(string jobId, CancellationToken cancellationToken);

    [Get("/api/missing/{id}")]
    Task<ApiResponse<MissingResponse>> GetMissingAsync(string id, CancellationToken cancellationToken);

    [Post("/api/races/{raceId}/conflict")]
    Task<ApiResponse<ConflictResponse>> ConflictAsync(string raceId, CancellationToken cancellationToken);

    [Get("/api/wait/{id}")]
    Task<ApiResponse<StatusResponse>> WaitAsync(string id, CancellationToken cancellationToken);

    [Get("/api/enums/{encoding}")]
    Task<ApiResponse<EnumResponse>> GetEnumAsync(string encoding, CancellationToken cancellationToken);
}

internal interface IRf015DirectBodyTransport
{
    [Post("/api/jobs/{jobId}/direct")]
    Task<IApiResponse> CompleteAsync([Body] CompleteRequest request, CancellationToken cancellationToken);
}

public sealed record StatusResponse(string Status);
public sealed record MissingResponse(string Value);
public sealed record ConflictResponse(string Value);
public sealed record EnumResponse(QueryStatus Status);

[JsonSerializable(typeof(GetRaceResponse))]
[JsonSerializable(typeof(CreateRaceRequest))]
[JsonSerializable(typeof(CreateRaceResponse))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(MissingResponse))]
[JsonSerializable(typeof(ConflictResponse))]
[JsonSerializable(typeof(EnumResponse))]
[JsonSerializable(typeof(RaceDto))]
internal partial class ProbeJsonContext : JsonSerializerContext;

internal sealed class NumericWriteStringOrNumberReadEnumConverter : JsonConverter<QueryStatus>
{
    public override QueryStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => Enum.Parse<QueryStatus>(reader.GetString()!, ignoreCase: false),
            JsonTokenType.Number => (QueryStatus)reader.GetInt32(),
            _ => throw new JsonException("Expected string or numeric QueryStatus")
        };

    public override void Write(Utf8JsonWriter writer, QueryStatus value, JsonSerializerOptions options)
        => writer.WriteNumberValue((int)value);
}

internal sealed class ProbeHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/wait/wait-1")
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        if (path == "/api/missing/gone")
            return Reply(request, new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"code\":\"Missing\"}") });
        if (path == "/api/races/race-3/conflict")
            return Reply(request, new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("{\"code\":\"Conflict\"}") });
        if (path == "/api/jobs/job-1/complete")
            return Reply(request, new HttpResponseMessage(HttpStatusCode.NoContent));
        if (path == "/api/races" && request.Method == HttpMethod.Post)
            return Reply(request, new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"createdId\":\"race-2\"}") });
        if (path == "/api/races/race-1")
            return Reply(request, new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"race\":{\"raceId\":\"race-1\",\"racecourseCode\":\"Tokyo\",\"raceDate\":\"2026-05-01\",\"status\":\"ResultDeclared\"}}")
            });
        if (path == "/api/enums/string")
            return Reply(request, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"ResultDeclared\"}") });
        if (path == "/api/enums/numeric")
            return Reply(request, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":1}") });
        return Reply(request, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"ok\"}") });
    }

    private static HttpResponseMessage Reply(HttpRequestMessage request, HttpResponseMessage response)
    {
        response.RequestMessage = request;
        return response;
    }
}
