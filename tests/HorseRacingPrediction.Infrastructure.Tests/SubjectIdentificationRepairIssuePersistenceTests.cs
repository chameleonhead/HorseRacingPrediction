using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HorseRacingPrediction.Infrastructure.Tests;

[TestClass]
public sealed class SubjectIdentificationRepairIssuePersistenceTests
{
    [TestMethod]
    public async Task SaveAndLoad_PreservesRepairEvidenceAndResolution()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<EventStoreDbContext>().UseSqlite(connection).Options;
        var issueId = Guid.NewGuid();
        var recoveryTaskId = Guid.NewGuid();

        await using (var context = new EventStoreDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
            context.SubjectIdentificationRepairIssues.Add(new SubjectIdentificationRepairIssue
            {
                IssueId = issueId,
                SubjectType = "horse",
                SubjectId = "legacy-horse",
                SubjectName = "テストホース",
                DefinitionId = "horse-profile",
                RequestedByRaceId = "race-1",
                SourceIdentity = "2026101234",
                SourceUrl = "https://example.test/horse/2026101234",
                ReasonCode = "SubjectProjectionNotReady",
                ReasonMessage = "The subject projection is not ready.",
                EvidenceFingerprint = "evidence-1",
                Status = "Resolved",
                CreatedAt = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.FromHours(9)),
                ResolvedAt = new DateTimeOffset(2026, 9, 20, 10, 5, 0, TimeSpan.FromHours(9)),
                TargetSubjectId = "canonical-horse",
                RecoveryTaskId = recoveryTaskId,
            });
            await context.SaveChangesAsync();
        }

        await using var verification = new EventStoreDbContext(options);
        var saved = await verification.SubjectIdentificationRepairIssues.SingleAsync(x => x.IssueId == issueId);
        Assert.AreEqual("evidence-1", saved.EvidenceFingerprint);
        Assert.AreEqual("canonical-horse", saved.TargetSubjectId);
        Assert.AreEqual(recoveryTaskId, saved.RecoveryTaskId);
        Assert.AreEqual(TimeSpan.FromHours(9), saved.CreatedAt.Offset);
        Assert.AreEqual(TimeSpan.FromHours(9), saved.ResolvedAt?.Offset);
    }

    [TestMethod]
    public async Task SaveDuplicateEvidenceFingerprint_IsRejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<EventStoreDbContext>().UseSqlite(connection).Options;
        await using var context = new EventStoreDbContext(options);
        await context.Database.EnsureCreatedAsync();

        context.SubjectIdentificationRepairIssues.Add(CreateIssue(Guid.NewGuid(), "same-evidence"));
        await context.SaveChangesAsync();
        context.SubjectIdentificationRepairIssues.Add(CreateIssue(Guid.NewGuid(), "same-evidence"));

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static SubjectIdentificationRepairIssue CreateIssue(Guid issueId, string fingerprint) => new()
    {
        IssueId = issueId,
        SubjectType = "jockey",
        SubjectId = "jockey-1",
        SubjectName = "騎手名",
        DefinitionId = "jockey-profile",
        ReasonCode = "SubjectProjectionNotReady",
        ReasonMessage = "The subject projection is not ready.",
        EvidenceFingerprint = fingerprint,
        Status = "Open",
        CreatedAt = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.FromHours(9)),
    };
}
