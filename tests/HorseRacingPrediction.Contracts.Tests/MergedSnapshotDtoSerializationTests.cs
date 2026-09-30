using System.Text.Json;

using HorseRacingPrediction.Contracts.Memos;
using HorseRacingPrediction.Contracts.Races;

namespace HorseRacingPrediction.Contracts.Tests;

[TestClass]
public sealed class MergedSnapshotDtoSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void MemoBySubjectDto_RoundTripsLegacySnapshotPayloadAndPreservesListOrder()
    {
        const string legacyJson = """
            {"subjectKey":"RACE:r-1","memos":[{"memoId":"m-1","authorId":null,"memoType":"Note","content":"Memo text","createdAt":"2026-05-01T09:00:00+00:00","subjects":[{"subjectType":"Race","subjectId":"r-1"},{"subjectType":"Horse","subjectId":"h-1"}],"links":[{"linkId":"l-1","linkType":"web","title":"Article","url":null,"storageKey":"store-1"}]}]}
            """;

        var response = JsonSerializer.Deserialize<MemoBySubjectDto>(legacyJson, JsonOptions)!;
        var roundTrip = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(response, JsonOptions));

        Assert.AreEqual("RACE:r-1", response.SubjectKey);
        Assert.AreEqual(1, response.Memos.Count);
        Assert.IsNull(response.Memos[0].AuthorId);
        Assert.AreEqual("Memo text", response.Memos[0].Content);
        Assert.AreEqual(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), response.Memos[0].CreatedAt);
        Assert.AreEqual(TimeSpan.Zero, response.Memos[0].CreatedAt.Offset);
        Assert.AreEqual(2, response.Memos[0].Subjects.Count);
        Assert.AreEqual("Race", response.Memos[0].Subjects[0].SubjectType);
        Assert.AreEqual("Horse", response.Memos[0].Subjects[1].SubjectType);
        Assert.AreEqual("Article", response.Memos[0].Links[0].Title);
        Assert.IsNull(response.Memos[0].Links[0].Url);
        Assert.AreEqual("store-1", response.Memos[0].Links[0].StorageKey);
        Assert.IsTrue(roundTrip.GetProperty("memos")[0].GetProperty("authorId").ValueKind == JsonValueKind.Null);
        Assert.IsTrue(roundTrip.GetProperty("memos")[0].GetProperty("links")[0].GetProperty("url").ValueKind == JsonValueKind.Null);
    }

    [TestMethod]
    public void RacePredictionContextDto_RoundTripsLegacyWeatherAndTrackSnapshotsInOrder()
    {
        const string legacyJson = """
            {"raceId":"r-1","weatherObservations":[{"observationTime":"2026-05-01T09:00:00+00:00","weatherCode":"sunny","weatherText":"晴れ","temperatureCelsius":20,"humidityPercent":50,"windDirectionCode":"N","windSpeedMeterPerSecond":2},{"observationTime":"2026-05-01T11:00:00+00:00","weatherCode":null,"weatherText":"曇り","temperatureCelsius":18,"humidityPercent":null,"windDirectionCode":"N","windSpeedMeterPerSecond":3}],"trackConditionObservations":[{"observationTime":"2026-05-01T09:00:00+00:00","turfConditionCode":"良","dirtConditionCode":"良","goingDescriptionText":"乾燥"},{"observationTime":"2026-05-01T11:00:00+00:00","turfConditionCode":"稍重","dirtConditionCode":null,"goingDescriptionText":"小雨"}]}
            """;

        var context = JsonSerializer.Deserialize<RacePredictionContextDto>(legacyJson, JsonOptions)!;
        var roundTrip = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(context, JsonOptions));

        Assert.AreEqual(2, context.WeatherObservations.Count);
        Assert.AreEqual(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), context.WeatherObservations[0].ObservationTime);
        Assert.AreEqual(TimeSpan.Zero, context.WeatherObservations[0].ObservationTime.Offset);
        Assert.AreEqual("sunny", context.WeatherObservations[0].WeatherCode);
        Assert.IsNull(context.LatestWeather!.WeatherCode);
        Assert.AreEqual(18m, context.LatestWeather.TemperatureCelsius);
        Assert.AreEqual(2, context.TrackConditionObservations.Count);
        Assert.AreEqual(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), context.TrackConditionObservations[0].ObservationTime);
        Assert.AreEqual(TimeSpan.Zero, context.TrackConditionObservations[0].ObservationTime.Offset);
        Assert.AreEqual("良", context.TrackConditionObservations[0].TurfConditionCode);
        Assert.AreEqual("稍重", context.LatestTrackCondition!.TurfConditionCode);
        Assert.IsNull(context.LatestTrackCondition.DirtConditionCode);
        Assert.IsTrue(roundTrip.GetProperty("weatherObservations")[1].GetProperty("weatherCode").ValueKind == JsonValueKind.Null);
        Assert.IsTrue(roundTrip.GetProperty("trackConditionObservations")[1].GetProperty("dirtConditionCode").ValueKind == JsonValueKind.Null);
        Assert.AreEqual("sunny", roundTrip.GetProperty("weatherObservations")[0].GetProperty("weatherCode").GetString());
    }
}
