using HorseRacingPrediction.Api.CollectionController;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using HorseRacingPrediction.Contracts.Collection;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionBackgroundSchedulerRegistrationTests
{
    [TestMethod]
    public void AddCollectionBackgroundSchedulers_Disabled_DoesNotRegisterExpansionServices()
    {
        var services = new ServiceCollection();

        services.AddCollectionBackgroundSchedulers(enabled: false, watchdogEnabled: false);

        Assert.IsEmpty(services.Where(x => x.ServiceType == typeof(IHostedService)));
    }

    [TestMethod]
    public void AddCollectionBackgroundSchedulers_Enabled_RegistersCoordinatorBackfillAndOneMinuteWatchdog()
    {
        var services = new ServiceCollection();

        services.AddCollectionBackgroundSchedulers(enabled: true);

        Assert.HasCount(3, services.Where(x => x.ServiceType == typeof(IHostedService)));
        Assert.AreEqual(1, services.Count(x => x.ServiceType == typeof(CollectionMaintenanceCoordinator)));
        Assert.AreEqual(1, services.Count(x => x.ServiceType == typeof(CollectionScheduleService)));
        Assert.AreEqual(1, services.Count(x => x.ServiceType == typeof(CollectionPlanningScheduler)));
    }

    [TestMethod]
    public void AddCollectionBackgroundSchedulers_OnlyWatchdogEnabled_RegistersIndependentWatchdog()
    {
        var services = new ServiceCollection();

        services.AddCollectionBackgroundSchedulers(enabled: false, watchdogEnabled: true, watchdogIntervalMinutes: 7);

        Assert.HasCount(1, services.Where(x => x.ServiceType == typeof(IHostedService)));
        var cadence = CollectionProducerCadencePolicy.GetWatchdogCadence(false, true, 7);
        Assert.IsTrue(cadence.Enabled);
        Assert.AreEqual(7, cadence.IntervalMinutes);
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<CollectionJobWatchdogOptions>>().Value;
        Assert.IsTrue(options.Enabled);
        Assert.AreEqual(7, options.IntervalMinutes);
    }

    [TestMethod]
    public void BackgroundSchedulersForceWatchdogRegistrationEvenWhenLegacyWatchdogFlagIsFalse()
    {
        var services = new ServiceCollection();

        services.AddCollectionBackgroundSchedulers(enabled: true, watchdogEnabled: false);

        Assert.HasCount(3, services.Where(x => x.ServiceType == typeof(IHostedService)));
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<CollectionJobWatchdogOptions>>().Value;
        Assert.IsTrue(options.Enabled);
        Assert.AreEqual(1, options.IntervalMinutes);
    }

    [TestMethod]
    public void RuntimeRecorder_ReflectsEffectiveRegistrationsAndDisabledWrapper()
    {
        var enabledServices = new ServiceCollection();
        enabledServices.AddCollectionBackgroundSchedulers(enabled: true, watchdogEnabled: false,
            watchdogIntervalMinutes: 9, queueEnabled: true, dispatchIntervalSeconds: 2,
            deadLetterReconcilerEnabled: true, deadLetterIntervalSeconds: 45,
            metricDeliveryEnabled: true, alertsEnabled: true);
        var enabled = enabledServices.BuildServiceProvider().GetRequiredService<CollectionRuntimeStatusRecorder>()
            .GetSnapshot().Runtime.Actions;

        Assert.HasCount(8, enabled);
        AssertAction(enabled, CollectionRuntimeAction.Dispatcher, true, TimeSpan.FromSeconds(2));
        AssertAction(enabled, CollectionRuntimeAction.DiscoveryPlanner, true, TimeSpan.FromMinutes(1));
        AssertAction(enabled, CollectionRuntimeAction.RefreshPlanner, true, TimeSpan.FromMinutes(1));
        AssertAction(enabled, CollectionRuntimeAction.TaskLeaseRecovery, true, TimeSpan.FromMinutes(1));
        AssertAction(enabled, CollectionRuntimeAction.BackfillRecovery, true, TimeSpan.FromMinutes(5));
        AssertAction(enabled, CollectionRuntimeAction.DeadLetterReconciliation, true, TimeSpan.FromSeconds(45));
        AssertAction(enabled, CollectionRuntimeAction.Alerts, true, TimeSpan.FromSeconds(5));
        AssertAction(enabled, CollectionRuntimeAction.MetricDelivery, true, null);

        var disabledServices = new ServiceCollection();
        disabledServices.AddCollectionBackgroundSchedulers(enabled: false, watchdogEnabled: false,
            queueEnabled: false, deadLetterReconcilerEnabled: false, metricDeliveryEnabled: false,
            alertsEnabled: true);
        var disabled = disabledServices.BuildServiceProvider().GetRequiredService<CollectionRuntimeStatusRecorder>()
            .GetSnapshot().Runtime.Actions;
        Assert.HasCount(8, disabled);
        foreach (var action in disabled.Where(x => x.Action != CollectionRuntimeAction.Alerts))
        {
            Assert.IsFalse(action.Enabled, $"{action.Action} should be disabled when its wrapper is disabled.");
            Assert.IsNull(action.EffectiveInterval);
        }
        AssertAction(disabled, CollectionRuntimeAction.Alerts, true, TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void MaintenanceMode_DisablesAllEightActionsAndHostedSchedulersDespiteEnabledInputs()
    {
        var services = new ServiceCollection();
        services.AddCollectionBackgroundSchedulers(
            enabled: true,
            watchdogEnabled: true,
            queueEnabled: true,
            deadLetterReconcilerEnabled: true,
            metricDeliveryEnabled: true,
            alertsEnabled: true,
            maintenanceMode: true);

        Assert.IsEmpty(services.Where(x => x.ServiceType == typeof(IHostedService)));
        using var provider = services.BuildServiceProvider();
        var actions = provider.GetRequiredService<CollectionRuntimeStatusRecorder>()
            .GetSnapshot().Runtime.Actions;
        Assert.HasCount(8, actions);
        foreach (var action in actions)
        {
            Assert.IsFalse(action.Enabled, $"{action.Action} must be disabled in maintenance mode.");
            Assert.IsNull(action.EffectiveInterval, $"{action.Action} must have no interval in maintenance mode.");
            Assert.AreEqual(CollectionRuntimeState.Disabled, action.State, action.Action.ToString());
        }
    }

    private static void AssertAction(IReadOnlyList<CollectionRuntimeActionStatusDto> actions,
        CollectionRuntimeAction action, bool enabled, TimeSpan? interval)
    {
        var actual = actions.Single(x => x.Action == action);
        Assert.AreEqual(enabled, actual.Enabled, action.ToString());
        Assert.AreEqual(interval, actual.EffectiveInterval, action.ToString());
    }
}
