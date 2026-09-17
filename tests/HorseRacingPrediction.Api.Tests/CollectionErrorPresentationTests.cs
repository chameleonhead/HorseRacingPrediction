using HorseRacingPrediction.Api.Web.Components.Shared.DesignSystem;

namespace HorseRacingPrediction.Api.Tests;

[TestClass]
public sealed class CollectionErrorPresentationTests
{
    [TestMethod]
    public void SubjectIdentityFailuresDoNotRecommendBlindRetry()
    {
        var ambiguous = CollectionErrorPresentation.Describe("SubjectNotIdentified",
            "公開検索に一致候補が複数あります。");
        var structural = CollectionErrorPresentation.Describe("JraCollectionException",
            "騎手情報の見出しを確認できません。");
        var invalidReference = CollectionErrorPresentation.Describe("SubjectNotIdentified",
            "公開検索に一致候補がありません。期待=Horse:パネットーネ 産駒");

        StringAssert.Contains(ambiguous.SuggestedAction, "自動再取得は行いません");
        StringAssert.Contains(structural.SuggestedAction, "再取得を繰り返さず");
        StringAssert.Contains(invalidReference.SuggestedAction, "再取得せず");
    }
}
