using HorseRacingPrediction.Api.CollectionController;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionBackgroundSchedulerRegistrationTests
{
    [TestMethod]
    public void AddCollectionBackgroundSchedulers_Disabled_DoesNotRegisterExpansionServices()
    {
        var services = new ServiceCollection();

        services.AddCollectionBackgroundSchedulers(enabled: false);

        Assert.IsEmpty(services.Where(x => x.ServiceType == typeof(IHostedService)));
    }

    [TestMethod]
    public void AddCollectionBackgroundSchedulers_Enabled_RegistersBothServices()
    {
        var services = new ServiceCollection();

        services.AddCollectionBackgroundSchedulers(enabled: true);

        Assert.HasCount(3, services.Where(x => x.ServiceType == typeof(IHostedService)));
    }
}
