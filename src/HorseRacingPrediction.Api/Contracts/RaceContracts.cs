using System.ComponentModel.DataAnnotations;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Api.Contracts;

public sealed record CreateRaceRequest(
    [property: Required] DateOnly RaceDate,
    [property: Required, StringLength(32, MinimumLength = 2)] string RacecourseCode,
    [property: Range(1, 20)] int RaceNumber,
    [property: Required, StringLength(128, MinimumLength = 1)] string RaceName);

public sealed record PublishRaceCardRequest(
    [property: Range(1, 40)] int EntryCount);

public sealed record DeclareRaceResultRequest(
    [property: Required, StringLength(128, MinimumLength = 1)] string WinningHorseName,
    DateTimeOffset? DeclaredAt);

public sealed record RaceResponse(
    string RaceId,
    DateOnly? RaceDate,
    string? RacecourseCode,
    int? RaceNumber,
    string? RaceName,
    RaceStatus Status,
    int? EntryCount,
    string? WinningHorseName,
    DateTimeOffset? ResultDeclaredAt);
