using HorseRacingPrediction.Domain.Horses;

namespace HorseRacingPrediction.Domain.Tests;

[TestClass]
public class HorseAggregateTests
{
    [TestMethod]
    public void RegisterHorse_SetsDetailsCorrectly()
    {
        var sut = new HorseAggregate(HorseId.New);

        sut.RegisterHorse("ディープインパクト", "ディープインパクト", sexCode: "M", birthDate: new DateOnly(2002, 3, 25));

        var details = sut.GetDetails();
        Assert.AreEqual("ディープインパクト", details.RegisteredName);
        Assert.AreEqual("ディープインパクト", details.NormalizedName);
        Assert.AreEqual("M", details.SexCode);
        Assert.AreEqual(new DateOnly(2002, 3, 25), details.BirthDate);
    }

    [TestMethod]
    public void RegisterHorse_WhenAlreadyRegistered_ThrowsInvalidOperationException()
    {
        var sut = new HorseAggregate(HorseId.New);
        sut.RegisterHorse("ディープインパクト", "ディープインパクト");

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.RegisterHorse("キタサンブラック", "キタサンブラック"));
    }

    [TestMethod]
    public void UpdateProfile_UpdatesSpecifiedFields()
    {
        var sut = new HorseAggregate(HorseId.New);
        sut.RegisterHorse("ディープインパクト", "ディープインパクト", sexCode: "M");

        sut.UpdateProfile(sexCode: "G");

        Assert.AreEqual("G", sut.GetDetails().SexCode);
        Assert.AreEqual("ディープインパクト", sut.GetDetails().RegisteredName);
    }

    [TestMethod]
    public void UpdateProfile_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        var sut = new HorseAggregate(HorseId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.UpdateProfile(registeredName: "テスト"));
    }

    [TestMethod]
    public void MergeAlias_AddsAlias()
    {
        var sut = new HorseAggregate(HorseId.New);
        sut.RegisterHorse("ディープインパクト", "ディープインパクト");

        sut.MergeAlias("JRA_CODE", "2002100816", "JRA", true);

        var details = sut.GetDetails();
        Assert.AreEqual(1, details.Aliases.Count);
        var alias = details.Aliases.First();
        Assert.AreEqual("JRA_CODE", alias.AliasType);
        Assert.AreEqual("2002100816", alias.AliasValue);
        Assert.AreEqual("JRA", alias.SourceName);
        Assert.IsTrue(alias.IsPrimary);
    }

    [TestMethod]
    public void MergeAlias_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        var sut = new HorseAggregate(HorseId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.MergeAlias("JRA_CODE", "123", "JRA", true));
    }

    [TestMethod]
    public void CorrectData_UpdatesSpecifiedFields()
    {
        var sut = new HorseAggregate(HorseId.New);
        sut.RegisterHorse("ディープインパクト", "ディープインパクト");

        sut.CorrectData(registeredName: "Deep Impact", reason: "英語名に修正");

        Assert.AreEqual("Deep Impact", sut.GetDetails().RegisteredName);
    }

    [TestMethod]
    public void CorrectData_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        var sut = new HorseAggregate(HorseId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.CorrectData(registeredName: "テスト"));
    }

    [TestMethod]
    public void GetDetails_ReturnsCorrectAggregateId()
    {
        var sut = new HorseAggregate(HorseId.New);
        Assert.AreEqual(sut.Id.Value, sut.GetDetails().HorseId);
    }

    [TestMethod]
    public void FullLifecycle_ProducesCorrectState()
    {
        var sut = new HorseAggregate(HorseId.New);

        sut.RegisterHorse("ディープインパクト", "ディープインパクト", sexCode: "M", birthDate: new DateOnly(2002, 3, 25));
        sut.MergeAlias("JRA_CODE", "2002100816", "JRA", true);
        sut.MergeAlias("NETKEIBA_ID", "2002100816", "netkeiba", false);
        sut.UpdateProfile(normalizedName: "deep impact");

        var details = sut.GetDetails();
        Assert.AreEqual("ディープインパクト", details.RegisteredName);
        Assert.AreEqual("deep impact", details.NormalizedName);
        Assert.AreEqual("M", details.SexCode);
        Assert.AreEqual(2, details.Aliases.Count);
    }

    [TestMethod]
    public void AddMemo_WhenRegistered_AddsSuccessfully()
    {
        var sut = new HorseAggregate(HorseId.New);
        sut.RegisterHorse("ディープインパクト", "ディープインパクト");
        var memoId = MemoId.New.Value;

        sut.AddMemo(memoId, "trainer-1", "TrainingComment", "好調です", DateTimeOffset.UtcNow);

        // 同じ ID のメモが削除後には UpdateMemo で例外になることで存在確認できる
        sut.DeleteMemo(memoId);
        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.UpdateMemo(memoId, content: "削除済みなので例外になるはず"));
    }

    [TestMethod]
    public void AddMemo_WhenNotRegistered_ThrowsInvalidOperationException()
    {
        var sut = new HorseAggregate(HorseId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.AddMemo(MemoId.New.Value, null, "GeneralNote", "テスト", DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void AddMemo_WithLinks_DoesNotThrow()
    {
        var sut = new HorseAggregate(HorseId.New);
        sut.RegisterHorse("ディープインパクト", "ディープインパクト");
        var memoId = MemoId.New.Value;
        var links = new List<HorseMemoLink>
        {
            new("link-1", HorseMemoLinkType.Url, "参考記事", "https://example.com/article", null),
            new("link-2", HorseMemoLinkType.Attachment, "調教動画", null, "videos/training-2024-01-01.mp4")
        };

        sut.AddMemo(memoId, "trainer-1", "TrainingComment", "参考リンク付きメモ", DateTimeOffset.UtcNow, links);

        // 存在しているので UpdateMemo は例外にならない
        sut.UpdateMemo(memoId, content: "更新OK");
    }

    [TestMethod]
    public void UpdateMemo_WhenMemoExists_DoesNotThrow()
    {
        var sut = new HorseAggregate(HorseId.New);
        sut.RegisterHorse("ディープインパクト", "ディープインパクト");
        var memoId = MemoId.New.Value;
        sut.AddMemo(memoId, "trainer-1", "TrainingComment", "初期内容", DateTimeOffset.UtcNow);

        sut.UpdateMemo(memoId, content: "更新された内容");
    }

    [TestMethod]
    public void UpdateMemo_WhenMemoNotFound_ThrowsInvalidOperationException()
    {
        var sut = new HorseAggregate(HorseId.New);
        sut.RegisterHorse("ディープインパクト", "ディープインパクト");

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.UpdateMemo("non-existent-memo-id", content: "更新"));
    }

    [TestMethod]
    public void DeleteMemo_WhenMemoExists_RemovesSuccessfully()
    {
        var sut = new HorseAggregate(HorseId.New);
        sut.RegisterHorse("ディープインパクト", "ディープインパクト");
        var memoId = MemoId.New.Value;
        sut.AddMemo(memoId, "trainer-1", "GeneralNote", "削除対象メモ", DateTimeOffset.UtcNow);

        sut.DeleteMemo(memoId);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.DeleteMemo(memoId));
    }

    [TestMethod]
    public void DeleteMemo_WhenMemoNotFound_ThrowsInvalidOperationException()
    {
        var sut = new HorseAggregate(HorseId.New);
        sut.RegisterHorse("ディープインパクト", "ディープインパクト");

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.DeleteMemo("non-existent-memo-id"));
    }
}
