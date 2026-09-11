using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class EntryResultDeclared : AggregateEvent<RaceAggregate, RaceId>
{
    public EntryResultDeclared(string entryId,
        int? finishPosition = null, string? officialTime = null,
        string? marginText = null, string? lastThreeFurlongTime = null,
        string? abnormalResultCode = null, decimal? prizeMoney = null,
        string? cornerPositions = null,
        int? popularity = null, int? originalFinishPosition = null, bool isDeadHeat = false, decimal? average1F = null, string? horseId = null, string? jockeyId = null,
        decimal? additionalPrizeMoney = null)
    {
        Popularity = popularity; OriginalFinishPosition = originalFinishPosition; IsDeadHeat = isDeadHeat; Average1F = average1F;
        HorseId = horseId; JockeyId = jockeyId;
        EntryId = entryId;
        FinishPosition = finishPosition;
        OfficialTime = officialTime;
        MarginText = marginText;
        LastThreeFurlongTime = lastThreeFurlongTime;
        AbnormalResultCode = abnormalResultCode;
        PrizeMoney = prizeMoney;
        AdditionalPrizeMoney = additionalPrizeMoney;
        CornerPositions = cornerPositions;
    }

    public string? HorseId { get; }
    public string? JockeyId { get; }
    public int? Popularity { get; }
    public int? OriginalFinishPosition { get; }
    public bool IsDeadHeat { get; }
    public decimal? Average1F { get; }
    public string EntryId { get; }
    public int? FinishPosition { get; }
    public string? OfficialTime { get; }
    public string? MarginText { get; }
    public string? LastThreeFurlongTime { get; }
    public string? AbnormalResultCode { get; }
    public decimal? PrizeMoney { get; }
    public decimal? AdditionalPrizeMoney { get; }
    public string? CornerPositions { get; }
}
