using EventFlow.Commands;
using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Application.Commands.Races;

public sealed class DeclareEntryResultCommand : Command<RaceAggregate, RaceId>
{
    public DeclareEntryResultCommand(RaceId aggregateId, string entryId,
        int? finishPosition = null, string? officialTime = null,
        string? marginText = null, string? lastThreeFurlongTime = null,
        string? abnormalResultCode = null, decimal? prizeMoney = null,
        string? cornerPositions = null)
        : base(aggregateId)
    {
        EntryId = entryId;
        FinishPosition = finishPosition;
        OfficialTime = officialTime;
        MarginText = marginText;
        LastThreeFurlongTime = lastThreeFurlongTime;
        AbnormalResultCode = abnormalResultCode;
        PrizeMoney = prizeMoney;
        CornerPositions = cornerPositions;
    }

    public string EntryId { get; }
    public int? FinishPosition { get; }
    public string? OfficialTime { get; }
    public string? MarginText { get; }
    public string? LastThreeFurlongTime { get; }
    public string? AbnormalResultCode { get; }
    public decimal? PrizeMoney { get; }
    public string? CornerPositions { get; }
}
