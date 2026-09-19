using System.Net;
using HorseRacingPrediction.CollectionOperations.CollectionPlatform;
using HorseRacingPrediction.Scraping.Jra.Pages;

namespace HorseRacingPrediction.Collector.CollectionPlatform;

internal static class ResourceLocationOutcomeClassifier
{
    public static ResourceLocationOutcome Unexpected(ResourceLocationCandidate location, string errorCode,
        RaceArtifactKind? artifact = null)
        => new(location.LocationId, CollectionAttemptResult.UnexpectedPage, errorCode, artifact);

    public static ResourceLocationOutcome Succeeded(ResourceLocationCandidate location,
        RaceArtifactKind? artifact = null)
        => new(location.LocationId, CollectionAttemptResult.Succeeded, Artifact: artifact);

    public static ResourceLocationOutcome Failed(ResourceLocationCandidate location, Exception exception,
        RaceArtifactKind? artifact = null)
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
        return new(location.LocationId, result, exception.GetType().Name, artifact);
    }
}
