using System.Net;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

internal static class ResourceLocationOutcomeClassifier
{
    public static ResourceLocationOutcome Unexpected(ResourceLocationCandidate location, string errorCode)
        => new(location.LocationId, CollectionAttemptResult.UnexpectedPage, errorCode);

    public static ResourceLocationOutcome Succeeded(ResourceLocationCandidate location)
        => new(location.LocationId, CollectionAttemptResult.Succeeded);

    public static ResourceLocationOutcome Failed(ResourceLocationCandidate location, Exception exception)
    {
        var result = exception switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.NotFound } => CollectionAttemptResult.ResourceNotFound,
            HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => CollectionAttemptResult.AccessLimited,
            HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } =>
                CollectionAttemptResult.TransientFailure,
            TimeoutException => CollectionAttemptResult.TransientFailure,
            JraPageParseException => CollectionAttemptResult.UnexpectedPage,
            _ => CollectionAttemptResult.TransientFailure,
        };
        return new(location.LocationId, result, exception.GetType().Name);
    }
}
