using HorseRacingPrediction.Contracts;

namespace HorseRacingPrediction.Contracts.Tests;

[TestClass]
public sealed class RacePredictionContextReadModelTests
{
    [TestMethod]
    public void LatestWeather_NoObservations_ReturnsNull()
    {
        var model = new RacePredictionContextReadModel();

        Assert.IsNull(model.LatestWeather);
    }

    [TestMethod]
    public void LatestWeather_ReturnsMostRecentByObservationTime()
    {
        var older = new WeatherObservationSnapshot(
            new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), "sunny", "晴れ", 20m, 50m, "N", 2m);
        var newer = new WeatherObservationSnapshot(
            new DateTimeOffset(2026, 5, 1, 11, 0, 0, TimeSpan.Zero), "cloudy", "曇り", 18m, 60m, "N", 3m);

        var model = new RacePredictionContextReadModel
        {
            WeatherObservations = [older, newer],
        };

        Assert.AreEqual(newer, model.LatestWeather);
    }

    [TestMethod]
    public void LatestTrackCondition_NoObservations_ReturnsNull()
    {
        var model = new RacePredictionContextReadModel();

        Assert.IsNull(model.LatestTrackCondition);
    }

    [TestMethod]
    public void LatestTrackCondition_ReturnsMostRecentByObservationTime()
    {
        var older = new TrackConditionSnapshot(
            new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), "良", "良", "乾燥");
        var newer = new TrackConditionSnapshot(
            new DateTimeOffset(2026, 5, 1, 11, 0, 0, TimeSpan.Zero), "稍重", "稍重", "小雨");

        var model = new RacePredictionContextReadModel
        {
            TrackConditionObservations = [older, newer],
        };

        Assert.AreEqual(newer, model.LatestTrackCondition);
    }
}
