using System.ComponentModel.DataAnnotations;

namespace HorseRacingPrediction.Contracts.Races;

public sealed record RaceResultEntryBulkDto(
    int? HorseNumber,
    int? FinishPosition,
    string? OfficialTime,
    string? MarginText,
    string? LastThreeFurlongTime,
    string? AbnormalResultCode,
    decimal? PrizeMoney,
    string? HorseName = null,
    string? JockeyName = null,
    string? TrainerName = null,
    int? GateNumber = null,
    decimal? AssignedWeight = null,
    string? SexCode = null,
    int? Age = null,
    int? Popularity = null,
    int? BodyWeight = null,
    int? BodyWeightChange = null,
    int? OriginalFinishPosition = null,
    bool IsDeadHeat = false, string? OwnerName = null, string? CornerPositions = null, decimal? Average1F = null,
    string? BreederName = null, string? SireName = null, string? DamName = null,
    string? DamsireName = null, string? CoatColor = null, decimal? AdditionalPrizeMoney = null,
    string? HorseSourceIdentity = null,
    string? JockeyProfileUrl = null,
    string? TrainerProfileUrl = null,
    RaceEntryParticipationStatus? ParticipationStatus = null);
