using HorseRacingPrediction.AgentClient.Scheduling;

namespace HorseRacingPrediction.AgentClient.Tests.Scheduling;

[TestClass]
public sealed class RaceDataCollectionErrorClassifierTests
{
    [TestMethod]
    public void Classify_WhenMessageContainsMetadataFailure_ReturnsMetadataMissing()
    {
        var result = RaceDataCollectionErrorClassifier.Classify("保存スキップ: https://example.test — 開催日・競馬場・レース番号の特定に失敗しました。");

        Assert.AreEqual(RaceDataCollectionErrorCode.MetadataMissing, result.Code);
    }

    [TestMethod]
    public void Classify_WhenExceptionIsHttpRequestException_ReturnsExternalRequestFailed()
    {
        var result = RaceDataCollectionErrorClassifier.Classify("remote request failed", new HttpRequestException("boom"));

        Assert.AreEqual(RaceDataCollectionErrorCode.ExternalRequestFailed, result.Code);
    }
}