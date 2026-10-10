using Bunit;
using HorseRacingPrediction.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HorseRacingPrediction.Web.Tests;

[TestClass]
public sealed class FilterComponentsTests
{
    [TestMethod]
    public async Task FilterSelection_TriggerIsAccessibleAndTogglesPopover()
    {
        using var context = CreateContext();
        var cut = context.Render<FilterSelection>(parameters => parameters
            .Add(x => x.Label, "開催場")
            .Add(x => x.Items, [new FilterSelectionOption("", "すべて"), new FilterSelectionOption("東京", "東京競馬場")]));

        var trigger = cut.Find("button[aria-haspopup='listbox']");
        Assert.AreEqual("false", trigger.GetAttribute("aria-expanded"));
        Assert.AreEqual("開催場: すべて", trigger.GetAttribute("aria-label"));

        await trigger.ClickAsync();

        Assert.AreEqual("true", cut.Find("button[aria-haspopup='listbox']").GetAttribute("aria-expanded"));
        Assert.IsTrue(cut.FindComponents<FluentPopover>().Single().Instance.Opened);
    }

    [TestMethod]
    public async Task FilterSelection_SingleSelectionNotifiesParentAndClosesPopover()
    {
        using var context = CreateContext();
        string? selectedValue = null;
        var cut = context.Render<FilterSelection>(parameters => parameters
            .Add(x => x.Label, "開催場")
            .Add(x => x.Items, [new FilterSelectionOption("", "すべて"), new FilterSelectionOption("東京", "東京競馬場")])
            .Add(x => x.ValueChanged, value => selectedValue = value));

        await cut.Find("button[aria-haspopup='listbox']").ClickAsync();
        var listbox = cut.FindComponent<FluentListbox<FilterSelectionOption, string>>();
        await cut.InvokeAsync(() => listbox.Instance.ValueChanged.InvokeAsync("東京"));

        Assert.AreEqual("東京", selectedValue);
        Assert.AreEqual("false", cut.Find("button[aria-haspopup='listbox']").GetAttribute("aria-expanded"));
    }

    [TestMethod]
    public async Task FilterSelection_MultipleSelectionNotifiesParentAndUpdatesSummary()
    {
        using var context = CreateContext();
        string[]? selectedValues = null;
        var cut = context.Render<FilterSelection>(parameters => parameters
            .Add(x => x.Label, "開催場")
            .Add(x => x.Multiple, true)
            .Add(x => x.Items, [new FilterSelectionOption("東京", "東京競馬場"), new FilterSelectionOption("中山", "中山競馬場")])
            .Add(x => x.SelectedValuesChanged, values => selectedValues = values));

        await cut.Find("button[aria-haspopup='listbox']").ClickAsync();
        var listbox = cut.FindComponent<FluentListbox<FilterSelectionOption, string>>();
        await cut.InvokeAsync(() => listbox.Instance.SelectedItemsChanged.InvokeAsync(
            [new FilterSelectionOption("東京", "東京競馬場"), new FilterSelectionOption("中山", "中山競馬場")]));

        CollectionAssert.AreEqual(new[] { "東京", "中山" }, selectedValues);
        cut.Render(parameters => parameters
            .Add(x => x.Label, "開催場")
            .Add(x => x.Multiple, true)
            .Add(x => x.Items, [new FilterSelectionOption("東京", "東京競馬場"), new FilterSelectionOption("中山", "中山競馬場")])
            .Add(x => x.SelectedValues, selectedValues));
        StringAssert.Contains(cut.Find("button[aria-haspopup='listbox']").TextContent, "2件選択");
    }

    [TestMethod]
    public async Task FilterDateRangePicker_EnablingRangeFocusesStartAndDisablingClearsDates()
    {
        using var context = CreateContext();
        DateOnly? from = new DateOnly(2026, 10, 1);
        DateOnly? to = new DateOnly(2026, 10, 31);
        var enabled = false;
        var cut = context.Render<FilterDateRangePicker>(parameters => parameters
            .Add(x => x.From, from)
            .Add(x => x.FromChanged, (DateOnly? value) => from = value)
            .Add(x => x.To, to)
            .Add(x => x.ToChanged, (DateOnly? value) => to = value)
            .Add(x => x.Enabled, enabled)
            .Add(x => x.EnabledChanged, value => enabled = value));

        await cut.Find("button[aria-haspopup='listbox']").ClickAsync();
        var listbox = cut.FindComponent<FluentListbox<FilterSelectionOption, string>>();
        await cut.InvokeAsync(() => listbox.Instance.ValueChanged.InvokeAsync("range"));

        Assert.IsTrue(enabled);
        cut.Render(parameters => parameters
            .Add(x => x.From, from)
            .Add(x => x.FromChanged, (DateOnly? value) => from = value)
            .Add(x => x.To, to)
            .Add(x => x.ToChanged, (DateOnly? value) => to = value)
            .Add(x => x.Enabled, enabled)
            .Add(x => x.EnabledChanged, (bool value) => enabled = value));
        Assert.AreEqual(2, cut.FindComponents<FluentDatePicker<DateOnly?>>().Count);
        Assert.AreEqual("開始日", cut.FindComponent<FluentDatePicker<DateOnly?>>().Instance.AriaLabel);
        Assert.IsTrue(cut.FindComponent<FluentDatePicker<DateOnly?>>().Instance.Autofocus);

        await cut.Find("button[aria-haspopup='listbox']").ClickAsync();
        listbox = cut.FindComponent<FluentListbox<FilterSelectionOption, string>>();
        await cut.InvokeAsync(() => listbox.Instance.ValueChanged.InvokeAsync(string.Empty));
        cut.Render(parameters => parameters
            .Add(x => x.From, from)
            .Add(x => x.FromChanged, (DateOnly? value) => from = value)
            .Add(x => x.To, to)
            .Add(x => x.ToChanged, (DateOnly? value) => to = value)
            .Add(x => x.Enabled, enabled)
            .Add(x => x.EnabledChanged, (bool value) => enabled = value));

        Assert.IsFalse(enabled);
        Assert.IsNull(from);
        Assert.IsNull(to);
        Assert.AreEqual(0, cut.FindComponents<FluentDatePicker<DateOnly?>>().Count);
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddFluentUIComponents();
        return context;
    }
}
