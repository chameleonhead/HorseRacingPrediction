using EventFlow.Aggregates;

namespace HorseRacingPrediction.Domain.Races;

public sealed class EntryResultDeclared : AggregateEvent<RaceAggregate, RaceId>
{
    public EntryResultDeclared(string entryId,
        int? finishPosition = null, string? officialTime = null,
        string? marginText = null, string? lastThreeFurlongTime = null,
        string? abnormalResultCode = null, decimal? prizeMoney = null)
    {
        EntryId = entryId;
        FinishPosition = finishPosition;
        OfficialTime = officialTime;
        MarginText = marginText;
        LastThreeFurlongTime = lastThreeFurlongTime;
        AbnormalResultCode = abnormalResultCode;
        PrizeMoney = prizeMoney;
    }

    public string EntryId { get; }
    public int? FinishPosition { get; }
    public string? OfficialTime { get; }
    public string? MarginText { get; }
    public string? LastThreeFurlongTime { get; }
    public string? AbnormalResultCode { get; }
    public decimal? PrizeMoney { get; }
}
