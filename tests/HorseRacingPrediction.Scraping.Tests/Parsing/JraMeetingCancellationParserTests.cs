using HorseRacingPrediction.Scraping.Browser.Snapshots;
using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Parsing;
using HorseRacingPrediction.Scraping.Tests.TestSupport;

namespace HorseRacingPrediction.Scraping.Tests.Parsing;

[TestClass]
public sealed class JraMeetingCancellationParserTests
{
    private static readonly DateOnly Date = new(2026, 9, 21);
    private static PageSnapshot Snapshot(string caption = "4回中山7日", string notice = "中山競馬は台風のため中止。代替競馬は9月22日に実施。")
        => SemanticSnapshotFactory.Create(JraMeetingCancellationParser.ProgrammeUrl(Date).AbsoluteUri,
            root: new()
            {
                Kind = PageContentKind.Document,
                Children = [
                new() { Kind = PageContentKind.Heading, HeadingLevel = 1, AccessibleName = "JRA" },
                new() { Kind = PageContentKind.Heading, HeadingLevel = 1, Text = "2026年9月21日（祝日・月曜） 競馬番組" },
                new() { Kind = PageContentKind.Paragraph, Text = "天候によって中止・延期があります。" }]
            },
            tables: [new() { Caption = caption, Rows = [new() { Cells = [new() { Text = notice, ColumnSpan = 3 }] }] }]);

    [TestMethod]
    public void ExplicitCancellation_ReturnsDatedMeetingEvidence_NotOtherCourse()
    {
        var snapshot = Snapshot();
        var evidence = JraMeetingCancellationParser.Parse(snapshot, Date, RaceCourse.Nakayama);
        Assert.IsNotNull(evidence);
        Assert.AreEqual(Date, evidence.Date);
        Assert.AreEqual(4, evidence.MeetingNumber);
        Assert.AreEqual(7, evidence.MeetingDay);
        Assert.AreEqual(snapshot.Url, evidence.SourceUrl);
        Assert.IsNull(JraMeetingCancellationParser.Parse(snapshot, Date, RaceCourse.Hanshin));
    }

    [TestMethod]
    [DataRow("中山競馬は台風のため中止の可能性があります。")]
    [DataRow("中山競馬は中止ではありません。")]
    [DataRow("阪神競馬は台風のため中止。")]
    [DataRow("第1レースは台風のため中止。")]
    [DataRow("天候によって中止・延期があります。")]
    public void AmbiguousOrOtherCancellation_IsNotEvidence(string notice)
        => Assert.IsNull(JraMeetingCancellationParser.Parse(Snapshot(notice: notice), Date, RaceCourse.Nakayama));

    [TestMethod]
    public void WrongDateHostHeadingDuplicateOrNormalRaceRows_AreNotEvidence()
    {
        var snapshot = Snapshot();
        Assert.IsNull(JraMeetingCancellationParser.Parse(snapshot, Date.AddDays(1), RaceCourse.Nakayama));
        Assert.IsNull(JraMeetingCancellationParser.Parse(snapshot with
        {
            Root = new()
            {
                Kind = PageContentKind.Document,
                Children = [new() { Kind = PageContentKind.Heading, HeadingLevel = 1, Text = "2026年9月22日 競馬番組" }]
            }
        }, Date, RaceCourse.Nakayama));
        Assert.IsNull(JraMeetingCancellationParser.Parse(snapshot with { Url = new("https://evil.test" + snapshot.Url.AbsolutePath) }, Date, RaceCourse.Nakayama));
        Assert.IsNull(JraMeetingCancellationParser.Parse(snapshot with { Root = new() { Kind = PageContentKind.Document } }, Date, RaceCourse.Nakayama));
        Assert.IsNull(JraMeetingCancellationParser.Parse(snapshot with { Tables = [snapshot.Tables[0], snapshot.Tables[0]] }, Date, RaceCourse.Nakayama));
        Assert.IsNull(JraMeetingCancellationParser.Parse(snapshot with { Tables = [snapshot.Tables[0] with { Rows = [snapshot.Tables[0].Rows[0], new() { Cells = [new() { Text = "1R" }] }] }] }, Date, RaceCourse.Nakayama));
        Assert.IsNull(JraMeetingCancellationParser.Parse(Snapshot(caption: "中山"), Date, RaceCourse.Nakayama));
    }
}
