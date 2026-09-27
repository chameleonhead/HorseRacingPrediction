using System.Net;
using Microsoft.AspNetCore.TestHost;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class MachineLearningEndpointsTests
{
    [TestMethod]
    public async Task TrainMlModel_RemainsOutsideFilteredWriteGroup()
    {
        var (app, client) = await TestApplicationFactory.CreateAsync();
        await using var application = app;
        using var http = client;
        http.DefaultRequestHeaders.Add("X-Api-Key", TestApplicationFactory.TestApiKey);

        using var unauthenticated = app.GetTestClient();
        using var rejected = await unauthenticated.PostAsync("/api/ml/train", content: null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, rejected.StatusCode);

        using var response = await http.PostAsync("/api/ml/train", content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(body, "訓練に使用できる完了済みレースがありません。");
    }
}
