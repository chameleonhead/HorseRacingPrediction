using System.Net;

namespace HorseRacingPrediction.ApiClient.Tests;

internal sealed record ApiClientTestCapturedResponse(HttpStatusCode StatusCode, string Body, string? Location = null);
