using Bunit;
using HorseRacingPrediction.Api.Web.Components.Shared;
using HorseRacingPrediction.Api.Web.Components.Shared.DesignSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class ComponentRenderingTests
{
    [TestMethod]
    public void RaceOpsAlert_RendersSemanticErrorSurface()
    {
        using var context = CreateContext();

        var cut = context.RenderComponent<RaceOpsAlert>(parameters => parameters
            .Add(x => x.Title, "保存できませんでした")
            .Add(x => x.Messages, ["理由を入力してください。"]));

        var alert = cut.Find("[role='alert']");
        Assert.IsTrue(alert.ClassList.Contains("raceops-alert-error"));
        Assert.AreEqual("assertive", alert.GetAttribute("aria-live"));
        StringAssert.Contains(alert.TextContent, "理由を入力してください。");
    }

    [TestMethod]
    public void UiState_Error_UsesSharedAlertComponent()
    {
        using var context = CreateContext();

        var cut = context.RenderComponent<UiState>(parameters => parameters
            .Add(x => x.Kind, "error")
            .Add(x => x.Title, "読み込めませんでした")
            .Add(x => x.Message, "再度お試しください。"));

        Assert.AreEqual(1, cut.FindAll(".raceops-alert-error[role='alert']").Count);
    }

    private static Bunit.TestContext CreateContext()
    {
        var context = new Bunit.TestContext();
        context.Services.AddFluentUIComponents();
        return context;
    }
}
