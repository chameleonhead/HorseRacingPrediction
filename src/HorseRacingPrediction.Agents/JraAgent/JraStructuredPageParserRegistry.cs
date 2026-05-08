using HorseRacingPrediction.Agents.Browser;

namespace HorseRacingPrediction.Agents.JraAgent;

public static class JraStructuredPageParserRegistry
{
    public static JraStructuredPageEnvelope Parse(JraPageKind kind, PageSnapshot snapshot)
    {
        return kind switch
        {
            JraPageKind.KeibaMenu => ToEnvelope(kind, snapshot.Url, new JraKeibaMenuParser().Parse(snapshot)),
            JraPageKind.ScheduleCalendar => ToEnvelope(kind, snapshot.Url, new JraScheduleCalendarParser().Parse(snapshot)),
            JraPageKind.HoldingList => ToEnvelope(kind, snapshot.Url, new JraHoldingListParser().Parse(snapshot)),
            JraPageKind.RaceList => ToEnvelope(kind, snapshot.Url, new JraRaceListParser().Parse(snapshot)),
            JraPageKind.ThisWeekFeature => ToEnvelope(kind, snapshot.Url, new JraThisWeekFeatureParser().Parse(snapshot)),
            JraPageKind.GradeOneSpecial => ToEnvelope(kind, snapshot.Url, new JraGradeOneSpecialParser().Parse(snapshot)),
            _ => new JraStructuredPageEnvelope(false, kind, snapshot.Url, null, [], JraPageParseConfidence.Low, [], "ページ種別に対応する structured parser が登録されていません。"),
        };
    }

    private static JraStructuredPageEnvelope ToEnvelope<T>(
        JraPageKind kind,
        string sourceUrl,
        JraStructuredPageParseResult<T> result)
        where T : class
        => new(result.Success, kind, sourceUrl, result.Data, result.Issues, result.Confidence, result.RecommendedNextLinks, result.Error);
}