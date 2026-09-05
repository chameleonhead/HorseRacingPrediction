using Bunit;
using HorseRacingPrediction.Api.Web.Components.Shared;
using HorseRacingPrediction.Api.Web.Components.Shared.DesignSystem;
using HorseRacingPrediction.Api.Contracts;
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

        var cut = context.Render<RaceOpsAlert>(parameters => parameters
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

        var cut = context.Render<UiState>(parameters => parameters
            .Add(x => x.Kind, "error")
            .Add(x => x.Title, "読み込めませんでした")
            .Add(x => x.Message, "再度お試しください。"));

        Assert.AreEqual(1, cut.FindAll(".raceops-alert-error[role='alert']").Count);
    }

    [TestMethod]
    public void RelationshipGrid_RankedMode_ShowsRankWinsAndPrizeMoney()
    {
        using var context = CreateContext();
        var relationships = new[]
        {
            new RelationshipSummaryResponse("horse", "horse-1", "テストホース", "騎乗馬", 12, new DateOnly(2026, 8, 30), 12_345_678m, 4)
        };

        var cut = context.Render<RaceOpsRelationshipGrid>(parameters => parameters
            .Add(x => x.SectionId, "ranking")
            .Add(x => x.Title, "上位馬")
            .Add(x => x.Ranked, true)
            .Add(x => x.Items, relationships)
            .Add(x => x.Url, relationship => $"/horses/{relationship.ObjectId}"));

        Assert.AreEqual("1", cut.Find(".rank").TextContent);
        StringAssert.Contains(cut.Find(".hint").TextContent, "4 勝");
        Assert.AreEqual("12,345,678円", cut.Find(".money").TextContent);
        Assert.AreEqual("/horses/horse-1", cut.Find("a").GetAttribute("href"));
    }

    private static Bunit.BunitContext CreateContext()
    {
        var context = new Bunit.BunitContext();
        context.Services.AddFluentUIComponents();
        return context;
    }
}
