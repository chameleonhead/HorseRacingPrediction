using System.Net;

namespace HorseRacingPrediction.Contracts.Identity;

/// <summary>A known lack of identity evidence, not a conflicting persisted identity.</summary>
public sealed class SubjectIdentityResolutionException : Exception
{
    public static bool IsKnownCode(string? code) =>
        code is "HorseIdentityEvidenceRequired" or "AmbiguousHorseIdentity";

    public SubjectIdentityResolutionException(string code, string subjectName, HttpStatusCode statusCode)
        : base($"Code={code}; Path=/api/identity/horse; SubjectType=Horse; SubjectName={subjectName}; Stage=ResolveIdentity")
    {
        if (!IsKnownCode(code)) throw new ArgumentException("Unknown identity resolution code.", nameof(code));
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public HttpStatusCode StatusCode { get; }
}
