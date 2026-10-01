namespace HorseRacingPrediction.ApiClient.Tests;

internal sealed record ApiClientTestCapturedRequest(HttpMethod Method, Uri? RequestUri, string? Body);
