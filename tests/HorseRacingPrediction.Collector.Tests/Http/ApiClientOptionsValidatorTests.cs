using HorseRacingPrediction.Collector.Http;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.Collector.Tests.Http;

[TestClass]
public sealed class ApiClientOptionsValidatorTests
{
    [TestMethod]
    public void Validate_WithMissingBaseUrlAndApiKey_ReturnsFailures()
    {
        var sut = new ApiClientOptionsValidator();

        var result = sut.Validate(name: null, new ApiClientOptions());

        Assert.AreEqual(ValidateOptionsResult.Fail(Array.Empty<string>()).Failed.GetType(), result.Failed.GetType());
        Assert.IsTrue(result.Failed, "validation should fail when BaseUrl and ApiKey are missing");
        Assert.IsTrue(result.Failures.Any(message => message.Contains("BaseUrl", StringComparison.Ordinal)));
        Assert.IsTrue(result.Failures.Any(message => message.Contains("ApiKey", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_WithValidSettings_Succeeds()
    {
        var sut = new ApiClientOptionsValidator();

        var result = sut.Validate(name: null, new ApiClientOptions
        {
            BaseUrl = "https://api.example.test",
            ApiKey = "test-key"
        });

        Assert.IsTrue(result.Succeeded);
    }
}