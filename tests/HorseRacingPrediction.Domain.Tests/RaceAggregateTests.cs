using HorseRacingPrediction.Domain.Races;

namespace HorseRacingPrediction.Domain.Tests;

[TestClass]
public class RaceAggregateTests
{
    [TestMethod]
    public void Create_SetsRaceDetailsCorrectly()
    {
        var sut = new RaceAggregate(RaceId.New);

        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        var details = sut.GetDetails();
        Assert.AreEqual(new DateOnly(2025, 6, 15), details.RaceDate);
        Assert.AreEqual("TOKYO", details.RacecourseCode);
        Assert.AreEqual(5, details.RaceNumber);
        Assert.AreEqual("皐月賞", details.RaceName);
        Assert.AreEqual(RaceStatus.Draft, details.Status);
    }

    [TestMethod]
    public void Create_WithExtendedFields_SetsAllProperties()
    {
        var sut = new RaceAggregate(RaceId.New);

        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞",
            meetingNumber: 3, dayNumber: 2, gradeCode: "G1",
            surfaceCode: "TURF", distanceMeters: 2000, directionCode: "RIGHT");

        var details = sut.GetDetails();
        Assert.AreEqual(3, details.MeetingNumber);
        Assert.AreEqual(2, details.DayNumber);
        Assert.AreEqual("G1", details.GradeCode);
        Assert.AreEqual("TURF", details.SurfaceCode);
        Assert.AreEqual(2000, details.DistanceMeters);
        Assert.AreEqual("RIGHT", details.DirectionCode);
    }

    [TestMethod]
    public void Create_WhenAlreadyCreated_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.Create(new DateOnly(2025, 7, 1), "NAKAYAMA", 1, "有馬記念"));
    }

    [TestMethod]
    public void PublishCard_FromDraft_SetsCardPublishedStatus()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        sut.PublishCard(18);

        var details = sut.GetDetails();
        Assert.AreEqual(RaceStatus.CardPublished, details.Status);
        Assert.AreEqual(18, details.EntryCount);
    }

    [TestMethod]
    public void PublishCard_WhenNotCreated_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);

        Assert.ThrowsException<InvalidOperationException>(() => sut.PublishCard(18));
    }

    [TestMethod]
    public void PublishCard_WhenAlreadyPublished_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);

        Assert.ThrowsException<InvalidOperationException>(() => sut.PublishCard(16));
    }

    [TestMethod]
    public void RegisterEntry_AfterCardPublished_AddsEntry()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);

        sut.RegisterEntry("entry-1", "horse-1", 1, jockeyId: "jockey-1", trainerId: "trainer-1",
            gateNumber: 1, assignedWeight: 57.0m, sexCode: "M", age: 3);

        var details = sut.GetDetails();
        Assert.AreEqual(1, details.Entries.Count);
        var entry = details.Entries[0];
        Assert.AreEqual("entry-1", entry.EntryId);
        Assert.AreEqual("horse-1", entry.HorseId);
        Assert.AreEqual(1, entry.HorseNumber);
        Assert.AreEqual("jockey-1", entry.JockeyId);
        Assert.AreEqual("trainer-1", entry.TrainerId);
        Assert.AreEqual(1, entry.GateNumber);
        Assert.AreEqual(57.0m, entry.AssignedWeight);
    }

    [TestMethod]
    public void RegisterEntry_WhenDraft_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.RegisterEntry("entry-1", "horse-1", 1));
    }

    [TestMethod]
    public void RecordWeatherObservation_SetsObservation()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        var observedAt = DateTimeOffset.UtcNow;

        sut.RecordWeatherObservation(observedAt, weatherCode: "SUNNY", temperatureCelsius: 22.5m);

        var details = sut.GetDetails();
        Assert.AreEqual(1, details.WeatherObservations.Count);
        Assert.AreEqual("SUNNY", details.WeatherObservations[0].WeatherCode);
        Assert.AreEqual(22.5m, details.WeatherObservations[0].TemperatureCelsius);
    }

    [TestMethod]
    public void RecordTrackConditionObservation_SetsObservation()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        var observedAt = DateTimeOffset.UtcNow;

        sut.RecordTrackConditionObservation(observedAt, turfConditionCode: "GOOD", goingDescriptionText: "良");

        var details = sut.GetDetails();
        Assert.AreEqual(1, details.TrackConditionObservations.Count);
        Assert.AreEqual("GOOD", details.TrackConditionObservations[0].TurfConditionCode);
        Assert.AreEqual("良", details.TrackConditionObservations[0].GoingDescriptionText);
    }

    [TestMethod]
    public void OpenPreRace_FromCardPublished_SetsPreRaceOpenStatus()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);

        sut.OpenPreRace();

        Assert.AreEqual(RaceStatus.PreRaceOpen, sut.GetDetails().Status);
    }

    [TestMethod]
    public void OpenPreRace_FromDraft_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        Assert.ThrowsException<InvalidOperationException>(() => sut.OpenPreRace());
    }

    [TestMethod]
    public void StartRace_FromPreRaceOpen_SetsInProgressStatus()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);
        sut.OpenPreRace();

        sut.StartRace();

        Assert.AreEqual(RaceStatus.InProgress, sut.GetDetails().Status);
    }

    [TestMethod]
    public void StartRace_FromCardPublished_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);

        Assert.ThrowsException<InvalidOperationException>(() => sut.StartRace());
    }

    [TestMethod]
    public void DeclareResult_FromCardPublished_SetsResultDeclaredStatus()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);
        var declaredAt = DateTimeOffset.UtcNow;

        sut.DeclareResult("ディープインパクト", declaredAt);

        var details = sut.GetDetails();
        Assert.AreEqual(RaceStatus.ResultDeclared, details.Status);
        Assert.AreEqual("ディープインパクト", details.WinningHorseName);
        Assert.AreEqual(declaredAt, details.ResultDeclaredAt);
    }

    [TestMethod]
    public void DeclareResult_WithExtendedFields_SetsAllProperties()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);
        var declaredAt = DateTimeOffset.UtcNow;

        sut.DeclareResult("ディープインパクト", declaredAt,
            winningHorseId: "horse-1", stewardReportText: "特記なし");

        var details = sut.GetDetails();
        Assert.AreEqual("horse-1", details.WinningHorseId);
        Assert.AreEqual("特記なし", details.StewardReportText);
    }

    [TestMethod]
    public void DeclareResult_WhenNotCreated_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.DeclareResult("ディープインパクト", DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void DeclareResult_FromDraft_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.DeclareResult("ディープインパクト", DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void DeclareResult_FromResultDeclared_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);
        sut.DeclareResult("ディープインパクト", DateTimeOffset.UtcNow);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.DeclareResult("キタサンブラック", DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void DeclareEntryResult_AfterRaceResult_AddsEntryResult()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);
        sut.DeclareResult("ディープインパクト", DateTimeOffset.UtcNow);

        sut.DeclareEntryResult("entry-1", finishPosition: 1, officialTime: "2:00.5",
            lastThreeFurlongTime: "34.2", prizeMoney: 200000000m);

        var details = sut.GetDetails();
        Assert.AreEqual(1, details.EntryResults.Count);
        Assert.AreEqual(1, details.EntryResults[0].FinishPosition);
        Assert.AreEqual("2:00.5", details.EntryResults[0].OfficialTime);
        Assert.AreEqual(200000000m, details.EntryResults[0].PrizeMoney);
    }

    [TestMethod]
    public void DeclareEntryResult_BeforeRaceResult_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.DeclareEntryResult("entry-1", finishPosition: 1));
    }

    [TestMethod]
    public void DeclarePayoutResult_FromResultDeclared_SetsPayoutDeclaredStatus()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);
        sut.DeclareResult("ディープインパクト", DateTimeOffset.UtcNow);
        var payoutTime = DateTimeOffset.UtcNow;

        var winPayouts = new[] { new PayoutEntry("1", 500m) };
        sut.DeclarePayoutResult(payoutTime, winPayouts: winPayouts);

        var details = sut.GetDetails();
        Assert.AreEqual(RaceStatus.PayoutDeclared, details.Status);
        Assert.IsNotNull(details.PayoutResult);
        Assert.AreEqual(1, details.PayoutResult.WinPayouts.Count);
        Assert.AreEqual(500m, details.PayoutResult.WinPayouts[0].Amount);
    }

    [TestMethod]
    public void DeclarePayoutResult_FromCardPublished_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.DeclarePayoutResult(DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void CloseRaceLifecycle_FromPayoutDeclared_SetsClosedStatus()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);
        sut.DeclareResult("ディープインパクト", DateTimeOffset.UtcNow);
        sut.DeclarePayoutResult(DateTimeOffset.UtcNow, winPayouts: new[] { new PayoutEntry("1", 500m) });

        sut.CloseRaceLifecycle();

        Assert.AreEqual(RaceStatus.Closed, sut.GetDetails().Status);
    }

    [TestMethod]
    public void CloseRaceLifecycle_FromResultDeclared_SetsClosedStatus()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);
        sut.DeclareResult("ディープインパクト", DateTimeOffset.UtcNow);

        sut.CloseRaceLifecycle();

        Assert.AreEqual(RaceStatus.Closed, sut.GetDetails().Status);
    }

    [TestMethod]
    public void CloseRaceLifecycle_FromDraft_ThrowsInvalidOperationException()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        Assert.ThrowsException<InvalidOperationException>(() => sut.CloseRaceLifecycle());
    }

    [TestMethod]
    public void CorrectRaceData_UpdatesSpecifiedFields()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");

        sut.CorrectRaceData(raceName: "日本ダービー", gradeCode: "G1", reason: "レース名訂正");

        var details = sut.GetDetails();
        Assert.AreEqual("日本ダービー", details.RaceName);
        Assert.AreEqual("G1", details.GradeCode);
        Assert.AreEqual("TOKYO", details.RacecourseCode);
    }

    [TestMethod]
    public void CorrectRaceData_AllowedFromAnyState()
    {
        var sut = new RaceAggregate(RaceId.New);
        sut.Create(new DateOnly(2025, 6, 15), "TOKYO", 5, "皐月賞");
        sut.PublishCard(18);
        sut.DeclareResult("ディープインパクト", DateTimeOffset.UtcNow);

        sut.CorrectRaceData(surfaceCode: "DIRT");

        Assert.AreEqual("DIRT", sut.GetDetails().SurfaceCode);
    }

    [TestMethod]
    public void GetDetails_ReturnsCorrectAggregateId()
    {
        var sut = new RaceAggregate(RaceId.New);
        Assert.AreEqual(sut.Id.Value, sut.GetDetails().RaceId);
    }

    [TestMethod]
    public void FullLifecycle_ProducesCorrectState()
    {
        var sut = new RaceAggregate(RaceId.New);
        var declaredAt = DateTimeOffset.UtcNow;

        sut.Create(new DateOnly(2025, 12, 28), "NAKAYAMA", 11, "有馬記念",
            meetingNumber: 5, dayNumber: 8, gradeCode: "G1",
            surfaceCode: "TURF", distanceMeters: 2500, directionCode: "RIGHT");
        sut.PublishCard(16);
        sut.RegisterEntry("entry-1", "horse-1", 1, jockeyId: "jockey-1");
        sut.RecordWeatherObservation(DateTimeOffset.UtcNow, weatherCode: "CLOUDY");
        sut.RecordTrackConditionObservation(DateTimeOffset.UtcNow, turfConditionCode: "GOOD");
        sut.OpenPreRace();
        sut.StartRace();
        sut.DeclareResult("イクイノックス", declaredAt, winningHorseId: "horse-1");
        sut.DeclareEntryResult("entry-1", finishPosition: 1, officialTime: "2:32.4");
        sut.DeclarePayoutResult(declaredAt, winPayouts: new[] { new PayoutEntry("1", 250m) });
        sut.CloseRaceLifecycle();

        var details = sut.GetDetails();
        Assert.AreEqual(new DateOnly(2025, 12, 28), details.RaceDate);
        Assert.AreEqual("NAKAYAMA", details.RacecourseCode);
        Assert.AreEqual(11, details.RaceNumber);
        Assert.AreEqual("有馬記念", details.RaceName);
        Assert.AreEqual(RaceStatus.Closed, details.Status);
        Assert.AreEqual("G1", details.GradeCode);
        Assert.AreEqual(2500, details.DistanceMeters);
        Assert.AreEqual(16, details.EntryCount);
        Assert.AreEqual(1, details.Entries.Count);
        Assert.AreEqual(1, details.WeatherObservations.Count);
        Assert.AreEqual(1, details.TrackConditionObservations.Count);
        Assert.AreEqual("イクイノックス", details.WinningHorseName);
        Assert.AreEqual("horse-1", details.WinningHorseId);
        Assert.AreEqual(1, details.EntryResults.Count);
        Assert.IsNotNull(details.PayoutResult);
    }
}
