namespace HorseRacingPrediction.Scraping.Jra.Models;

/// <summary>Explicit cancellation of an entire dated meeting, not a missing race or publication delay.</summary>
public sealed record JraMeetingCancellation(DateOnly Date, RaceCourse Course,
    int MeetingNumber, int MeetingDay, Uri SourceUrl);
