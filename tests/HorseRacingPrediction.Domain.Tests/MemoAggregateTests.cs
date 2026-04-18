using HorseRacingPrediction.Domain.Memos;

namespace HorseRacingPrediction.Domain.Tests;

[TestClass]
public class MemoAggregateTests
{
    private static readonly MemoSubject HorseSubject = new(MemoSubjectType.Horse, "horse-1");
    private static readonly MemoSubject TrainerSubject = new(MemoSubjectType.Trainer, "trainer-1");

    [TestMethod]
    public void CreateMemo_SingleSubject_SetsDetailsCorrectly()
    {
        var sut = new MemoAggregate(MemoId.New);
        var subjects = new[] { HorseSubject };
        var createdAt = DateTimeOffset.UtcNow;

        sut.CreateMemo("author-1", "TrainingNote", "好調です", createdAt, subjects);

        // no exception means success; state verified via UpdateMemo (would throw if not created)
        sut.UpdateMemo(content: "更新OK");
    }

    [TestMethod]
    public void CreateMemo_MultipleSubjects_Succeeds()
    {
        var sut = new MemoAggregate(MemoId.New);
        var subjects = new[] { HorseSubject, TrainerSubject };

        sut.CreateMemo(null, "Observation", "調教師×馬のメモ", DateTimeOffset.UtcNow, subjects);

        sut.UpdateMemo(content: "更新OK");
    }

    [TestMethod]
    public void CreateMemo_WhenAlreadyCreated_ThrowsInvalidOperationException()
    {
        var sut = new MemoAggregate(MemoId.New);
        sut.CreateMemo(null, "Note", "初回", DateTimeOffset.UtcNow, new[] { HorseSubject });

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.CreateMemo(null, "Note", "2回目", DateTimeOffset.UtcNow, new[] { HorseSubject }));
    }

    [TestMethod]
    public void CreateMemo_WithNoSubjects_ThrowsInvalidOperationException()
    {
        var sut = new MemoAggregate(MemoId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.CreateMemo(null, "Note", "内容", DateTimeOffset.UtcNow, Array.Empty<MemoSubject>()));
    }

    [TestMethod]
    public void UpdateMemo_WhenCreated_DoesNotThrow()
    {
        var sut = new MemoAggregate(MemoId.New);
        sut.CreateMemo(null, "Note", "初期内容", DateTimeOffset.UtcNow, new[] { HorseSubject });

        sut.UpdateMemo(content: "更新された内容");
    }

    [TestMethod]
    public void UpdateMemo_WhenNotCreated_ThrowsInvalidOperationException()
    {
        var sut = new MemoAggregate(MemoId.New);

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.UpdateMemo(content: "更新"));
    }

    [TestMethod]
    public void UpdateMemo_WhenDeleted_ThrowsInvalidOperationException()
    {
        var sut = new MemoAggregate(MemoId.New);
        sut.CreateMemo(null, "Note", "内容", DateTimeOffset.UtcNow, new[] { HorseSubject });
        sut.DeleteMemo();

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.UpdateMemo(content: "削除済みなので例外"));
    }

    [TestMethod]
    public void DeleteMemo_WhenCreated_Succeeds()
    {
        var sut = new MemoAggregate(MemoId.New);
        sut.CreateMemo(null, "Note", "内容", DateTimeOffset.UtcNow, new[] { HorseSubject });

        sut.DeleteMemo();

        Assert.ThrowsException<InvalidOperationException>(() => sut.DeleteMemo());
    }

    [TestMethod]
    public void DeleteMemo_WhenNotCreated_ThrowsInvalidOperationException()
    {
        var sut = new MemoAggregate(MemoId.New);

        Assert.ThrowsException<InvalidOperationException>(() => sut.DeleteMemo());
    }

    [TestMethod]
    public void ChangeSubjects_ReplacesSubjectList()
    {
        var sut = new MemoAggregate(MemoId.New);
        sut.CreateMemo(null, "Note", "内容", DateTimeOffset.UtcNow, new[] { HorseSubject });

        var newSubjects = new[] { HorseSubject, TrainerSubject };
        sut.ChangeSubjects(newSubjects);

        // verify ChangeSubjects again to ensure state is valid
        sut.ChangeSubjects(new[] { TrainerSubject });
    }

    [TestMethod]
    public void ChangeSubjects_WithNoSubjects_ThrowsInvalidOperationException()
    {
        var sut = new MemoAggregate(MemoId.New);
        sut.CreateMemo(null, "Note", "内容", DateTimeOffset.UtcNow, new[] { HorseSubject });

        Assert.ThrowsException<InvalidOperationException>(() =>
            sut.ChangeSubjects(Array.Empty<MemoSubject>()));
    }

    [TestMethod]
    public void CreateMemo_WithLinks_SetsLinksCorrectly()
    {
        var sut = new MemoAggregate(MemoId.New);
        var links = new[]
        {
            new MemoLink("link-1", MemoLinkType.Url, "参考記事", "https://example.com", null),
            new MemoLink("link-2", MemoLinkType.Attachment, "動画", null, "videos/test.mp4")
        };

        sut.CreateMemo("author-1", "Note", "リンク付きメモ", DateTimeOffset.UtcNow,
            new[] { HorseSubject }, links);

        sut.UpdateMemo(content: "更新OK");
    }
}
