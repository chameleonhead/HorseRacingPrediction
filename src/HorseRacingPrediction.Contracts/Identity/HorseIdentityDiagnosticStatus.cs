namespace HorseRacingPrediction.Contracts.Identity;

/// <summary>Stable evidence states exposed by the read-only identity diagnostic.</summary>
public enum HorseIdentityDiagnosticStatus
{
    Supported,
    Missing,
    Invalid,
    Unsupported,
    Conflicting,
    Truncated,
}
