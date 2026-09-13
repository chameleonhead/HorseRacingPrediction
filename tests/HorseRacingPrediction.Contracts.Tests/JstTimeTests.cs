using System.Text.Json;
using HorseRacingPrediction.Contracts.Time;

namespace HorseRacingPrediction.Contracts.Tests;

[TestClass]
public sealed class JstTimeTests
{
    [TestMethod]
    public void Convert_CrossesTheJstDateBoundary()
    {
        var utc = new DateTimeOffset(2026, 9, 13, 15, 30, 0, TimeSpan.Zero);

        var actual = JstTime.Convert(utc);

        Assert.AreEqual(new DateTimeOffset(2026, 9, 14, 0, 30, 0, TimeSpan.FromHours(9)), actual);
    }

    [TestMethod]
    public void DatabaseRoundTrip_StoresNoTimezoneAndMaterializesAsJst()
    {
        var utc = new DateTimeOffset(2026, 9, 13, 15, 30, 0, TimeSpan.Zero);

        var stored = JstTime.ToDatabase(utc);
        var materialized = JstTime.FromDatabase(stored);

        Assert.AreEqual(DateTimeKind.Unspecified, stored.Kind);
        Assert.AreEqual(new DateTime(2026, 9, 14, 0, 30, 0), stored);
        Assert.AreEqual(TimeSpan.FromHours(9), materialized.Offset);
        Assert.AreEqual(utc, materialized);
    }

    [TestMethod]
    public void JsonConverter_WritesJstAndRejectsOffsetlessInput()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JstDateTimeOffsetJsonConverter());
        var utc = new DateTimeOffset(2026, 9, 13, 15, 30, 0, TimeSpan.Zero);

        var json = JsonSerializer.Serialize(utc, options);
        var roundTrip = JsonSerializer.Deserialize<DateTimeOffset>(json, options);

        Assert.AreEqual("2026-09-14T00:30:00.0000000+09:00", JsonSerializer.Deserialize<string>(json));
        Assert.AreEqual(TimeSpan.FromHours(9), roundTrip.Offset);
        Assert.ThrowsExactly<JsonException>(() =>
            JsonSerializer.Deserialize<DateTimeOffset>("\"2026-09-14T00:30:00\"", options));
    }

    [TestMethod]
    public void Format_AlwaysLabelsJst()
    {
        var utc = new DateTimeOffset(2026, 9, 13, 15, 30, 0, TimeSpan.Zero);

        Assert.AreEqual("2026/09/14 00:30 JST", JstTime.Format(utc));
    }

    [TestMethod]
    public void StoredJsonConverter_WritesNoTimezoneAndReadsLegacyUtcAsJst()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JstStoredDateTimeOffsetJsonConverter());
        var utc = new DateTimeOffset(2026, 9, 13, 15, 30, 0, TimeSpan.Zero);

        var json = JsonSerializer.Serialize(utc, options);
        var current = JsonSerializer.Deserialize<DateTimeOffset>(json, options);
        var legacy = JsonSerializer.Deserialize<DateTimeOffset>("\"2026-09-13T15:30:00+00:00\"", options);

        Assert.AreEqual("2026-09-14T00:30:00.0000000", JsonSerializer.Deserialize<string>(json));
        Assert.AreEqual(TimeSpan.FromHours(9), current.Offset);
        Assert.AreEqual(current, legacy);
    }
}
