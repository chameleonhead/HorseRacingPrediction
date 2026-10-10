using Bunit;
using HorseRacingPrediction.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
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
    [DataRow("today", "2026-10-07", "2026-10-07", true, "今日")]
    [DataRow("last-10-days", "2026-09-28", "2026-10-07", true, "10日以内")]
    [DataRow("last-month", "2026-09-07", "2026-10-07", true, "1か月以内")]
    [DataRow("last-year", "2025-10-07", "2026-10-07", true, "1年以内")]
    [DataRow("all", null, null, false, "すべて")]
    public async Task FilterDateRangePicker_PresetAppliesResolvedDates(
        string option,
        string? expectedFrom,
        string? expectedTo,
        bool expectedEnabled,
        string expectedSummary)
    {
        using var context = CreateContext();
        DateOnly? from = null;
        DateOnly? to = null;
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
        await cut.InvokeAsync(() => listbox.Instance.ValueChanged.InvokeAsync(option));

        Assert.AreEqual(expectedEnabled, enabled);
        cut.Render(parameters => parameters
            .Add(x => x.From, from)
            .Add(x => x.FromChanged, (DateOnly? value) => from = value)
            .Add(x => x.To, to)
            .Add(x => x.ToChanged, (DateOnly? value) => to = value)
            .Add(x => x.Enabled, enabled)
            .Add(x => x.EnabledChanged, (bool value) => enabled = value));
        Assert.AreEqual(expectedFrom is null ? null : DateOnly.Parse(expectedFrom), from);
        Assert.AreEqual(expectedTo is null ? null : DateOnly.Parse(expectedTo), to);
        StringAssert.Contains(cut.Find("button[aria-haspopup='listbox']").TextContent, expectedSummary);
    }

    [TestMethod]
    public async Task FilterDateRangePicker_CustomRangeUsesDrawerAndFormatsBadge()
    {
        using var context = CreateContext();
        DateOnly? from = null;
        DateOnly? to = null;
        var enabled = false;
        var cut = context.Render<FilterDateRangePicker>(parameters => parameters
            .Add(x => x.From, from)
            .Add(x => x.FromChanged, (DateOnly? value) => from = value)
            .Add(x => x.To, to)
            .Add(x => x.ToChanged, (DateOnly? value) => to = value)
            .Add(x => x.Enabled, enabled)
            .Add(x => x.EnabledChanged, (bool value) => enabled = value));

        await cut.Find("button[aria-haspopup='listbox']").ClickAsync();
        var listbox = cut.FindComponent<FluentListbox<FilterSelectionOption, string>>();
        await cut.InvokeAsync(() => listbox.Instance.ValueChanged.InvokeAsync("custom"));

        var dialog = cut.FindComponent<FluentDialog>().Instance;
        Assert.AreEqual(DialogAlignment.End, dialog.Alignment);
        var datePickers = cut.FindComponents<FluentDatePicker<DateOnly?>>();
        Assert.AreEqual(2, datePickers.Count);
        Assert.AreEqual("開始日", datePickers[0].Instance.AriaLabel);
        Assert.IsTrue(datePickers[0].Instance.Autofocus);
        Assert.AreEqual("終了日", datePickers[1].Instance.AriaLabel);

        await cut.InvokeAsync(() => datePickers[0].Instance.ValueChanged.InvokeAsync(new DateOnly(2026, 10, 11)));
        await cut.InvokeAsync(() => cut.FindComponents<FluentDatePicker<DateOnly?>>().Last().Instance.ValueChanged.InvokeAsync(new DateOnly(2026, 10, 10)));
        StringAssert.Contains(cut.Markup, "終了日は開始日以降の日付を指定してください。");
        Assert.IsTrue(cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("適用")).Instance.Disabled);

        await cut.InvokeAsync(() => cut.FindComponents<FluentDatePicker<DateOnly?>>().First().Instance.ValueChanged.InvokeAsync(new DateOnly(2026, 10, 1)));
        var apply = cut.FindComponents<FluentButton>().Single(button => button.Markup.Contains("適用"));
        await cut.InvokeAsync(() => apply.Instance.OnClick.InvokeAsync());

        Assert.AreEqual(new DateOnly(2026, 10, 1), from);
        Assert.AreEqual(new DateOnly(2026, 10, 10), to);
        Assert.IsTrue(enabled);

        cut.Render(parameters => parameters
            .Add(x => x.From, from)
            .Add(x => x.FromChanged, (DateOnly? value) => from = value)
            .Add(x => x.To, to)
            .Add(x => x.ToChanged, (DateOnly? value) => to = value)
            .Add(x => x.Enabled, enabled)
            .Add(x => x.EnabledChanged, (bool value) => enabled = value));
        StringAssert.Contains(cut.Find("button[aria-haspopup='listbox']").TextContent, "2026/10/01 - 2026/10/10");
    }

    [TestMethod]
    public async Task FilterDateRangePicker_OptionsCanBeSimplified()
    {
        using var context = CreateContext();
        var options = new[] { new FilterSelectionOption("all", "すべて"), new FilterSelectionOption("custom", "期間を指定する") };
        var cut = context.Render<FilterDateRangePicker>(parameters => parameters
            .Add(x => x.Options, options));

        await cut.Find("button[aria-haspopup='listbox']").ClickAsync();

        var optionsInList = cut.FindComponents<FluentListbox<FilterSelectionOption, string>>().Single().Instance.Items;
        Assert.AreEqual(2, optionsInList?.Count() ?? 0);
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddFluentUIComponents();
        context.Services.AddSingleton<TimeProvider>(new FrozenTimeProvider(new DateTimeOffset(2026, 10, 7, 2, 0, 0, TimeSpan.Zero)));
        return context;
    }

    private sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
