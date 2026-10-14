using System.Text.Json.Nodes;
using EventFlow;
using EventFlow.Aggregates;
using EventFlow.EntityFramework.Extensions;
using EventFlow.EventStores;
using EventFlow.EntityFramework;
using EventFlow.EntityFramework.EventStores;
using EventFlow.Extensions;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Memos;
using HorseRacingPrediction.Application.Commands.Predictions;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Memos;
using HorseRacingPrediction.Domain.Predictions;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using HorseRacingPrediction.Infrastructure.Persistence.HorseIdentityMigration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HorseRacingPrediction.Infrastructure.Tests;

[TestClass]
public sealed class HorseIdentityMigrationEventTransformerTests
{
    private const string RegisteredHorseName = "同名の競走馬";

    [TestMethod]
    public async Task TransformsRegisteredHorseAndRaceEntriesAndReplaysWithMetadataPreserved()
    {
        using var fixture = await EventFixture.CreateAsync();
        var oldHorseId = HorseIdFor(1);
        var newHorseId = HorseIdFor(2);
        var raceId = new RaceId("race-00000000-0000-4000-8000-000000000123");

        await fixture.CommandBus.PublishAsync(
            new RegisterHorseCommand(new HorseId(oldHorseId), RegisteredHorseName, "同名の競走馬"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2026, 10, 14), "TOKYO", 1, "検証レース"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new PublishRaceCardCommand(raceId, 12), CancellationToken.None);

        var derivedEntryId = DeterministicEntryId(raceId.Value, oldHorseId);
        await fixture.CommandBus.PublishAsync(
            new RegisterEntryCommand(raceId, derivedEntryId, oldHorseId, 4, jockeyId: "jockey-keep", ownerName: $"owner {oldHorseId}"),
            CancellationToken.None);
        await fixture.CommandBus.PublishAsync(
            new RegisterEntryCommand(raceId, derivedEntryId, oldHorseId, 4, jockeyId: "jockey-keep", ownerName: $"owner {oldHorseId}"),
            CancellationToken.None);

        var horseRecord = await fixture.ReadRecordAsync(oldHorseId, nameof(HorseAggregate), 1);
        var horseResult = fixture.Transformer(new Dictionary<string, string> { [oldHorseId] = newHorseId }).Transform(horseRecord);
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Transformed, horseResult.Status, $"reason={horseResult.Reason}");
        Assert.IsNotNull(horseResult.Event);
        Assert.AreEqual(newHorseId, horseResult.Event.AggregateId);
        AssertMetadataPreservedExceptAggregateId(horseRecord.Metadata, horseResult.Event.Metadata, oldHorseId, newHorseId);

        await fixture.ReplaceRecordAsync(horseResult.Event);
        var migratedHorse = await fixture.AggregateStore.LoadAsync<HorseAggregate, HorseId>(new HorseId(newHorseId), CancellationToken.None);
        Assert.AreEqual(RegisteredHorseName, migratedHorse.GetDetails().RegisteredName);

        var raceRecord = await fixture.ReadRecordAsync(raceId.Value, nameof(RaceAggregate), 4);
        var raceMetadataBefore = JsonNode.Parse(raceRecord.Metadata)!.DeepClone();
        var raceResult = fixture.Transformer(new Dictionary<string, string> { [oldHorseId] = newHorseId }).Transform(raceRecord);
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Transformed, raceResult.Status);
        Assert.IsNotNull(raceResult.Event);
        Assert.AreEqual(raceRecord.AggregateId, raceResult.Event.AggregateId);
        Assert.AreEqual(raceRecord.GlobalSequenceNumber, raceResult.Event.GlobalSequenceNumber);
        Assert.AreEqual(raceRecord.AggregateSequenceNumber, raceResult.Event.AggregateSequenceNumber);

        var before = JsonNode.Parse(raceRecord.Data)!.AsObject();
        var after = JsonNode.Parse(raceResult.Event.Data)!.AsObject();
        Assert.AreEqual(newHorseId, StringProperty(after, "horseId"));
        Assert.AreEqual(newHorseId, StringProperty(after, "previousHorseId"));
        Assert.AreEqual(DeterministicEntryId(raceId.Value, newHorseId), StringProperty(after, "entryId"));
        Assert.AreEqual($"owner {oldHorseId}", StringProperty(after, "ownerName"));
        Assert.AreEqual("jockey-keep", StringProperty(after, "jockeyId"));
        Assert.IsTrue(JsonNode.DeepEquals(before[FindPropertyName(before, "horseNumber")],
            after[FindPropertyName(after, "horseNumber")]), "Non-identity numeric fields must retain their serialized value.");
        AssertMetadataUnchanged(raceMetadataBefore, JsonNode.Parse(raceResult.Event.Metadata)!);

        var firstEntryRecord = await fixture.ReadRecordAsync(raceId.Value, nameof(RaceAggregate), 3);
        var firstEntryResult = fixture.Transformer(new Dictionary<string, string> { [oldHorseId] = newHorseId })
            .Transform(firstEntryRecord);
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Transformed, firstEntryResult.Status,
            $"reason={firstEntryResult.Reason}");
        Assert.IsNotNull(firstEntryResult.Event);
        await fixture.ReplaceRecordAsync(firstEntryResult.Event);
        await fixture.ReplaceRecordAsync(raceResult.Event);
        var replayedRace = await fixture.AggregateStore.LoadAsync<RaceAggregate, RaceId>(raceId, CancellationToken.None);
        var details = replayedRace.GetDetails();
        Assert.AreEqual(1, details.Entries.Count);
        Assert.IsTrue(details.Entries.All(entry => entry.HorseId == newHorseId),
            string.Join(",", details.Entries.Select(entry => entry.HorseId)));
        Assert.IsTrue(details.Entries.All(entry => entry.EntryId == DeterministicEntryId(raceId.Value, newHorseId)));
        Assert.IsTrue(details.Entries.All(entry => entry.JockeyId == "jockey-keep"));
        Assert.IsTrue(details.Entries.All(entry => entry.OwnerName == $"owner {oldHorseId}"));
    }

    [TestMethod]
    public async Task TransformsHorseReferencesButPreservesGenuineCallerEntryId()
    {
        using var fixture = await EventFixture.CreateAsync();
        var oldHorseId = HorseIdFor(3);
        var newHorseId = HorseIdFor(4);
        var raceId = new RaceId("race-00000000-0000-4000-8000-000000000124");
        await fixture.CommandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2026, 10, 14), "TOKYO", 2, "手動 ID レース"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new PublishRaceCardCommand(raceId, 12), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(
            new RegisterEntryCommand(raceId, "manual-entry-key", oldHorseId, 7), CancellationToken.None);

        var record = await fixture.ReadRecordAsync(raceId.Value, nameof(RaceAggregate), 3);
        var result = fixture.Transformer(new Dictionary<string, string> { [oldHorseId] = newHorseId }).Transform(record);

        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Transformed, result.Status, $"reason={result.Reason}");
        Assert.IsNotNull(result.Event);
        var data = JsonNode.Parse(result.Event.Data)!.AsObject();
        Assert.AreEqual(newHorseId, StringProperty(data, "horseId"));
        Assert.AreEqual("manual-entry-key", StringProperty(data, "entryId"));
    }

    [TestMethod]
    public async Task UnmappedSameNameHorseAndLiteralIdTextRemainUnchanged()
    {
        using var fixture = await EventFixture.CreateAsync();
        var mappedHorseId = HorseIdFor(5);
        var otherHorseId = HorseIdFor(6);
        await fixture.CommandBus.PublishAsync(
            new RegisterHorseCommand(new HorseId(mappedHorseId), RegisteredHorseName, "同名の競走馬"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(
            new RegisterHorseCommand(new HorseId(otherHorseId), RegisteredHorseName, "同名の競走馬"), CancellationToken.None);

        var otherHorseRecord = await fixture.ReadRecordAsync(otherHorseId, nameof(HorseAggregate), 1);
        var transformer = fixture.Transformer(new Dictionary<string, string> { [mappedHorseId] = HorseIdFor(7) });
        var result = transformer.Transform(otherHorseRecord);

        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Unchanged, result.Status);
        Assert.AreSame(otherHorseRecord, result.Event);

        var raceId = new RaceId("race-00000000-0000-4000-8000-000000000125");
        await fixture.CommandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2026, 10, 14), "TOKYO", 3, "テキスト保持"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new PublishRaceCardCommand(raceId, 12), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(
            new RegisterEntryCommand(raceId, "caller-key", mappedHorseId, 2, ownerName: $"記録 {mappedHorseId}"), CancellationToken.None);
        var record = await fixture.ReadRecordAsync(raceId.Value, nameof(RaceAggregate), 3);
        var transformed = fixture.Transformer(new Dictionary<string, string> { [mappedHorseId] = HorseIdFor(7) }).Transform(record);
        Assert.IsNotNull(transformed.Event);
        var data = JsonNode.Parse(transformed.Event.Data)!.AsObject();
        Assert.AreEqual($"記録 {mappedHorseId}", StringProperty(data, "ownerName"));
        Assert.AreEqual("caller-key", StringProperty(data, "entryId"));
    }

    [TestMethod]
    public async Task TransformsOtherSupportedRaceEventTypesThroughRegisteredEventFlowSerializer()
    {
        using var fixture = await EventFixture.CreateAsync();
        var oldHorseId = HorseIdFor(80);
        var newHorseId = HorseIdFor(81);
        var raceId = new RaceId("race-00000000-0000-4000-8000-000000000180");
        var derivedEntryId = DeterministicEntryId(raceId.Value, oldHorseId);
        await fixture.CommandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2026, 10, 14), "TOKYO", 8, "各種イベント"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new PublishRaceCardCommand(raceId, 1), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new RegisterEntryCommand(
            raceId, derivedEntryId, oldHorseId, 4, jockeyId: "jockey-preserved", ownerName: "owner-preserved"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new UpdateEntryCollectedDataCommand(
            raceId, derivedEntryId, 55.5m, -1.5m, "owner-updated"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new RecordRaceOddsSnapshotCommand(
            raceId, DateTimeOffset.Parse("2026-10-14T03:00:00Z"), [new RaceOddsEntry(4, 2.5m, 1)]), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new DeclareRaceResultCommand(
            raceId, "同名保持", DateTimeOffset.Parse("2026-10-14T04:00:00Z"), oldHorseId, "report-preserved"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new DeclareEntryResultCommand(
            raceId, derivedEntryId, 1, "1:23.4", marginText: "鼻"), CancellationToken.None);

        var transformer = fixture.Transformer(new Dictionary<string, string> { [oldHorseId] = newHorseId });
        var collected = transformer.Transform(await fixture.ReadRecordOfTypeAsync<EntryCollectedDataUpdated>(raceId.Value));
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Transformed, collected.Status, $"{collected.Reason}");
        var collectedData = JsonNode.Parse(collected.Event!.Data)!.AsObject();
        Assert.AreEqual(newHorseId, StringProperty(collectedData, "horseId"));
        Assert.AreEqual(55.5m, collectedData[FindPropertyName(collectedData, "declaredWeight")]!.GetValue<decimal>());
        Assert.AreEqual("owner-updated", StringProperty(collectedData, "ownerName"));
        Assert.AreEqual(DeterministicEntryId(raceId.Value, newHorseId), StringProperty(collectedData, "entryId"));

        var winner = transformer.Transform(await fixture.ReadRecordOfTypeAsync<RaceResultDeclared>(raceId.Value));
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Transformed, winner.Status, $"{winner.Reason}");
        var winnerData = JsonNode.Parse(winner.Event!.Data)!.AsObject();
        Assert.AreEqual(newHorseId, StringProperty(winnerData, "winningHorseId"));
        Assert.AreEqual("同名保持", StringProperty(winnerData, "winningHorseName"));
        Assert.AreEqual("report-preserved", StringProperty(winnerData, "stewardReportText"));

        var result = transformer.Transform(await fixture.ReadRecordOfTypeAsync<EntryResultDeclared>(raceId.Value));
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Transformed, result.Status, $"{result.Reason}");
        var resultData = JsonNode.Parse(result.Event!.Data)!.AsObject();
        Assert.AreEqual(newHorseId, StringProperty(resultData, "horseId"));
        Assert.AreEqual(DeterministicEntryId(raceId.Value, newHorseId), StringProperty(resultData, "entryId"));
        Assert.AreEqual("1:23.4", StringProperty(resultData, "officialTime"));
        Assert.AreEqual("鼻", StringProperty(resultData, "marginText"));

        var odds = transformer.Transform(await fixture.ReadRecordOfTypeAsync<RaceOddsSnapshotRecorded>(raceId.Value));
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Transformed, odds.Status, $"{odds.Reason}");
        var oddsData = JsonNode.Parse(odds.Event!.Data)!.AsObject();
        var assignment = oddsData[FindPropertyName(oddsData, "assignments")]!.AsArray().Single()!.AsObject();
        var oddsEntry = oddsData[FindPropertyName(oddsData, "entries")]![0]!.AsObject();
        Assert.AreEqual(newHorseId, StringProperty(assignment, "horseId"));
        Assert.AreEqual(DeterministicEntryId(raceId.Value, newHorseId), StringProperty(assignment, "entryId"));
        Assert.AreEqual(2.5m, oddsEntry[FindPropertyName(oddsEntry, "winOdds")]!.GetValue<decimal>());
    }

    [TestMethod]
    public async Task TransformsRepairEventTypedEntryListsWithoutChangingRepairEvidence()
    {
        using var fixture = await EventFixture.CreateAsync();
        var oldHorseId = HorseIdFor(82);
        var newHorseId = HorseIdFor(83);
        var raceId = new RaceId("race-00000000-0000-4000-8000-000000000181");
        var entryId = DeterministicEntryId(raceId.Value, oldHorseId);
        await fixture.CommandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2026, 10, 14), "TOKYO", 9, "修復イベント"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new PublishRaceCardCommand(raceId, 1), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new RegisterEntryCommand(
            raceId, entryId, oldHorseId, 1, gateNumber: 1, ownerName: "owner-before"), CancellationToken.None);
        var aggregate = await fixture.AggregateStore.LoadAsync<RaceAggregate, RaceId>(raceId, CancellationToken.None);
        var currentEntry = aggregate.GetDetails().Entries.Single();
        var repairedEntry = currentEntry with { OwnerName = "owner-after" };
        await fixture.CommandBus.PublishAsync(new RepairRaceEntryAssignmentsCommand(
            raceId, aggregate.Version, "operation-test", "fingerprint-test",
            "https://www.jra.go.jp/JRADB/accessD.html?CNAME=test", DateTimeOffset.Parse("2026-10-14T05:00:00Z"),
            [repairedEntry], "G1", "{\"sourceEvidence\":\"retained\"}"), CancellationToken.None);

        var record = await fixture.ReadRecordOfTypeAsync<RaceEntryAssignmentsRepaired>(raceId.Value);
        var result = fixture.Transformer(new Dictionary<string, string> { [oldHorseId] = newHorseId }).Transform(record);
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Transformed, result.Status, $"{result.Reason}");
        var transformed = JsonNode.Parse(result.Event!.Data)!.AsObject();
        Assert.AreEqual(newHorseId, StringProperty(transformed[FindPropertyName(transformed, "previousEntries")]![0]!.AsObject(), "horseId"));
        Assert.AreEqual(newHorseId, StringProperty(transformed[FindPropertyName(transformed, "entries")]![0]!.AsObject(), "horseId"));
        Assert.AreEqual(DeterministicEntryId(raceId.Value, newHorseId),
            StringProperty(transformed[FindPropertyName(transformed, "entries")]![0]!.AsObject(), "entryId"));
        Assert.AreEqual("fingerprint-test", StringProperty(transformed, "fingerprint"));
        Assert.AreEqual("{\"sourceEvidence\":\"retained\"}", StringProperty(transformed, "sourceEvidenceJson"));
        Assert.AreEqual("owner-after", StringProperty(transformed[FindPropertyName(transformed, "entries")]![0]!.AsObject(), "ownerName"));
        Assert.AreEqual("owner-before", StringProperty(transformed[FindPropertyName(transformed, "previousEntries")]![0]!.AsObject(), "ownerName"));
    }

    [TestMethod]
    public async Task TransformsAllMemoSubjectListsAndPreservesNonHorseFields()
    {
        using var fixture = await EventFixture.CreateAsync();
        var oldHorseId = HorseIdFor(90);
        var newHorseId = HorseIdFor(91);
        var memoId = new MemoId("memo-00000000-0000-4000-8000-000000000190");
        var link = new MemoLink("link-1", MemoLinkType.Url,
            $"title {oldHorseId}", $"https://example.test/{oldHorseId}", oldHorseId);

        await fixture.CommandBus.PublishAsync(new CreateMemoCommand(
            memoId, "author-1", "Note", $"created {oldHorseId}",
            DateTimeOffset.Parse("2026-10-14T01:00:00Z"),
            [new(MemoSubjectType.Horse, oldHorseId), new(MemoSubjectType.Trainer, oldHorseId)],
            [link]), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new UpdateMemoCommand(
            memoId, content: $"updated {oldHorseId}", links: [link]), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new ChangeMemoSubjectsCommand(memoId,
            [new(MemoSubjectType.Horse, oldHorseId), new(MemoSubjectType.Jockey, oldHorseId)]),
            CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new DeleteMemoCommand(memoId), CancellationToken.None);

        var records = await fixture.ReadRecordsAsync(memoId.Value, nameof(MemoAggregate));
        Assert.AreEqual(4, records.Count);
        var transformer = fixture.Transformer(new Dictionary<string, string> { [oldHorseId] = newHorseId });

        foreach (var record in records)
        {
            var result = transformer.Transform(record);
            Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Transformed, result.Status,
                $"{fixture.EventType(record)}: {result.Reason}");
            Assert.IsNotNull(result.Event);
            await fixture.ReplaceRecordAsync(result.Event);
        }

        var created = JsonNode.Parse(transformer.Transform(records[0]).Event!.Data)!.AsObject();
        AssertSubjectId(created, "subjects", 0, newHorseId);
        AssertSubjectId(created, "subjects", 1, oldHorseId);
        Assert.AreEqual($"created {oldHorseId}", StringProperty(created, "content"));
        var createdLink = ArrayProperty(created, "links")[0]!.AsObject();
        Assert.AreEqual($"https://example.test/{oldHorseId}", StringProperty(createdLink, "url"));
        Assert.AreEqual(oldHorseId, StringProperty(createdLink, "storageKey"));

        var updated = JsonNode.Parse(transformer.Transform(records[1]).Event!.Data)!.AsObject();
        AssertSubjectId(updated, "currentSubjects", 0, newHorseId);
        AssertSubjectId(updated, "currentSubjects", 1, oldHorseId);
        Assert.AreEqual($"updated {oldHorseId}", StringProperty(updated, "content"));

        var changed = JsonNode.Parse(transformer.Transform(records[2]).Event!.Data)!.AsObject();
        AssertSubjectId(changed, "addedSubjects", 0, oldHorseId);
        AssertSubjectId(changed, "removedSubjects", 0, oldHorseId);
        AssertSubjectId(changed, "allSubjects", 0, newHorseId);
        AssertSubjectId(changed, "allSubjects", 1, oldHorseId);

        var deleted = JsonNode.Parse(transformer.Transform(records[3]).Event!.Data)!.AsObject();
        AssertSubjectId(deleted, "subjects", 0, newHorseId);
        AssertSubjectId(deleted, "subjects", 1, oldHorseId);

        var unknownMemoType = JsonNode.Parse(records[0].Data)!.AsObject();
        ArrayProperty(unknownMemoType, "subjects")[0]!.AsObject()[FindPropertyName(
            ArrayProperty(unknownMemoType, "subjects")[0]!.AsObject(), "subjectType")] = 999;
        var unknownTypeResult = transformer.Transform(records[0] with { Data = unknownMemoType.ToJsonString() });
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Rejected, unknownTypeResult.Status);
        Assert.AreEqual(TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape, unknownTypeResult.Reason);

        var nullMemoSubject = JsonNode.Parse(records[0].Data)!.AsObject();
        var nullSubject = ArrayProperty(nullMemoSubject, "subjects")[0]!.AsObject();
        nullSubject[FindPropertyName(nullSubject, "subjectId")] = null;
        var nullSubjectResult = transformer.Transform(records[0] with { Data = nullMemoSubject.ToJsonString() });
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Rejected, nullSubjectResult.Status);
        Assert.AreEqual(TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape, nullSubjectResult.Reason);

        _ = await fixture.AggregateStore.LoadAsync<MemoAggregate, MemoId>(memoId, CancellationToken.None);
    }

    [TestMethod]
    public async Task TransformsTicketMarksAndHorseRationalesOnlyWithValidatedStreamContext()
    {
        using var fixture = await EventFixture.CreateAsync();
        var oldHorseId = HorseIdFor(92);
        var newHorseId = HorseIdFor(93);
        var raceId = "race-00000000-0000-4000-8000-000000000192";
        var ticketId = new PredictionTicketId("predictionticket-00000000-0000-4000-8000-000000000192");
        var deterministicEntryId = DeterministicEntryId(raceId, oldHorseId);

        await fixture.CommandBus.PublishAsync(new CreatePredictionTicketCommand(
            ticketId, raceId, "AI", "model-v1", .85m, $"summary {oldHorseId}",
            $"fingerprint-{oldHorseId}"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new AddPredictionMarkCommand(
            ticketId, deterministicEntryId, "◎", 1, 95m, $"mark {oldHorseId}"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new AddPredictionMarkCommand(
            ticketId, $"manual-{oldHorseId}", "○", 2, 80m, null), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new AddPredictionRationaleCommand(
            ticketId, "Horse", oldHorseId, "SpeedFigure", $"signal {oldHorseId}",
            $"explanation {oldHorseId}"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new AddPredictionRationaleCommand(
            ticketId, "Trainer", oldHorseId, "Stable", $"signal {oldHorseId}",
            $"explanation {oldHorseId}"), CancellationToken.None);

        var records = await fixture.ReadRecordsAsync(ticketId.Value, nameof(PredictionTicketAggregate));
        Assert.AreEqual(5, records.Count);
        Assert.AreEqual(1, records.Count(record => fixture.GetAggregateEvent(record) is PredictionTicketCreated));
        var createdRecord = records.Single(record => fixture.GetAggregateEvent(record) is PredictionTicketCreated);
        var createdEvent = (PredictionTicketCreated)fixture.GetAggregateEvent(createdRecord);
        var context = new TypedHorseIdentityTicketStreamContext(
            ticketId.Value, createdRecord.AggregateSequenceNumber, createdEvent.RaceId);
        var transformer = fixture.Transformer(new Dictionary<string, string> { [oldHorseId] = newHorseId });

        var createdResult = transformer.Transform(createdRecord, context);
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Unchanged, createdResult.Status,
            createdResult.Reason.ToString());
        Assert.AreSame(createdRecord, createdResult.Event);
        Assert.AreEqual($"fingerprint-{oldHorseId}", createdEvent.EntryAssignmentFingerprint);

        var deterministicMark = records.Single(record => fixture.GetAggregateEvent(record) is PredictionMarkAdded
            mark && mark.EntryId == deterministicEntryId);
        var deterministicResult = transformer.Transform(deterministicMark, context);
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Transformed, deterministicResult.Status,
            deterministicResult.Reason.ToString());
        var deterministicData = JsonNode.Parse(deterministicResult.Event!.Data)!.AsObject();
        Assert.AreEqual(DeterministicEntryId(raceId, newHorseId), StringProperty(deterministicData, "entryId"));
        Assert.AreEqual($"mark {oldHorseId}", StringProperty(deterministicData, "comment"));

        var manualMark = records.Single(record => fixture.GetAggregateEvent(record) is PredictionMarkAdded
            mark && mark.EntryId == $"manual-{oldHorseId}");
        var manualResult = transformer.Transform(manualMark, context);
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Unchanged, manualResult.Status);
        Assert.AreSame(manualMark, manualResult.Event);

        var horseRationale = records.Single(record => fixture.GetAggregateEvent(record) is PredictionRationaleAdded
            rationale && rationale.SubjectType == "Horse");
        var horseRationaleResult = transformer.Transform(horseRationale, context);
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Transformed, horseRationaleResult.Status,
            horseRationaleResult.Reason.ToString());
        var rationaleData = JsonNode.Parse(horseRationaleResult.Event!.Data)!.AsObject();
        Assert.AreEqual(newHorseId, StringProperty(rationaleData, "subjectId"));
        Assert.AreEqual($"signal {oldHorseId}", StringProperty(rationaleData, "signalValue"));
        Assert.AreEqual($"explanation {oldHorseId}", StringProperty(rationaleData, "explanationText"));

        var trainerRationale = records.Single(record => fixture.GetAggregateEvent(record) is PredictionRationaleAdded
            rationale && rationale.SubjectType == "Trainer");
        var trainerResult = transformer.Transform(trainerRationale, context);
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Unchanged, trainerResult.Status);
        Assert.AreSame(trainerRationale, trainerResult.Event);

        var missingContext = transformer.Transform(deterministicMark);
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Rejected, missingContext.Status);
        Assert.IsNull(missingContext.Event);
        var wrongContext = transformer.Transform(deterministicMark,
            context with { AggregateId = "ticket-other" });
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Rejected, wrongContext.Status);
        Assert.IsNull(wrongContext.Event);
        var lateCreatedContext = transformer.Transform(deterministicMark,
            context with { CreatedAggregateSequenceNumber = deterministicMark.AggregateSequenceNumber });
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Rejected, lateCreatedContext.Status);
        Assert.IsNull(lateCreatedContext.Event);

        var nullEntryIdData = JsonNode.Parse(deterministicMark.Data)!.AsObject();
        nullEntryIdData[FindPropertyName(nullEntryIdData, "entryId")] = null;
        var nullEntryIdResult = transformer.Transform(
            deterministicMark with { Data = nullEntryIdData.ToJsonString() }, context);
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Rejected, nullEntryIdResult.Status);
        Assert.AreEqual(TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape, nullEntryIdResult.Reason);

        var emptyEntryIdData = JsonNode.Parse(deterministicMark.Data)!.AsObject();
        emptyEntryIdData[FindPropertyName(emptyEntryIdData, "entryId")] = string.Empty;
        var emptyEntryIdResult = transformer.Transform(
            deterministicMark with { Data = emptyEntryIdData.ToJsonString() }, context);
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Rejected, emptyEntryIdResult.Status);
        Assert.AreEqual(TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape, emptyEntryIdResult.Reason);

        foreach (var result in records.Select(record => transformer.Transform(record, context)))
            if (result.Status == TypedHorseIdentityEventTransformStatus.Transformed)
                await fixture.ReplaceRecordAsync(result.Event!);

        var replayed = await fixture.AggregateStore.LoadAsync<PredictionTicketAggregate, PredictionTicketId>(
            ticketId, CancellationToken.None);
        var details = replayed.GetDetails();
        Assert.IsTrue(details.Marks.Any(mark => mark.EntryId == DeterministicEntryId(raceId, newHorseId)));
        Assert.IsTrue(details.Marks.Any(mark => mark.EntryId == $"manual-{oldHorseId}"));
        Assert.IsTrue(details.Rationales.Any(rationale => rationale.SubjectType == "Horse"
            && rationale.SubjectId == newHorseId
            && rationale.SignalValue == $"signal {oldHorseId}"
            && rationale.ExplanationText == $"explanation {oldHorseId}"));
        Assert.IsTrue(details.Rationales.Any(rationale => rationale.SubjectType == "Trainer"
            && rationale.SubjectId == oldHorseId));
    }

    [TestMethod]
    public async Task RejectsMissingNullAndInvalidPredictionTicketCreatedTimestamp()
    {
        using var fixture = await EventFixture.CreateAsync();
        var ticketId = new PredictionTicketId("predictionticket-00000000-0000-4000-8000-000000000193");
        var raceId = "race-00000000-0000-4000-8000-000000000193";
        await fixture.CommandBus.PublishAsync(new CreatePredictionTicketCommand(
            ticketId, raceId, "AI", "model-v1", .85m, "timestamp validation", "fingerprint"),
            CancellationToken.None);

        var createdRecord = (await fixture.ReadRecordsAsync(
            ticketId.Value, nameof(PredictionTicketAggregate))).Single();
        var createdEvent = (PredictionTicketCreated)fixture.GetAggregateEvent(createdRecord);
        var context = new TypedHorseIdentityTicketStreamContext(
            ticketId.Value, createdRecord.AggregateSequenceNumber, createdEvent.RaceId);
        var transformer = fixture.Transformer(new Dictionary<string, string>
        {
            [HorseIdFor(94)] = HorseIdFor(95),
        });

        var valid = transformer.Transform(createdRecord, context);
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Unchanged, valid.Status,
            valid.Reason.ToString());
        Assert.AreSame(createdRecord, valid.Event);
        Assert.AreEqual(createdRecord.Data, valid.Event!.Data);
        Assert.AreEqual(createdRecord.Metadata, valid.Event.Metadata);

        var timestampName = FindPropertyName(JsonNode.Parse(createdRecord.Data)!.AsObject(), "predictedAt");
        var malformedValues = new (string Name, Action<JsonObject> Mutate,
            TypedHorseIdentityEventTransformReason ExpectedReason)[]
        {
            ("missing", data => data.Remove(timestampName), TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape),
            ("null", data => data[timestampName] = null, TypedHorseIdentityEventTransformReason.InvalidJson),
            ("invalid", data => data[timestampName] = "not-a-timestamp", TypedHorseIdentityEventTransformReason.InvalidJson),
            ("locale-dependent", data => data[timestampName] = "10/14/2026 13:01:08 +09:00", TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape),
            ("date-only", data => data[timestampName] = "2026-10-14", TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape),
            ("unqualified-offset", data => data[timestampName] = "2026-10-14T13:01:08.1234567", TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape),
            ("unknown-created-field", data => data["unmodeledCreatedField"] = "unexpected", TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape),
            ("other-created-field-null", data => data[FindPropertyName(data, "confidenceScore")] = null, TypedHorseIdentityEventTransformReason.InvalidJson),
        };

        foreach (var (name, mutate, expectedReason) in malformedValues)
        {
            var data = JsonNode.Parse(createdRecord.Data)!.AsObject();
            mutate(data);
            var result = transformer.Transform(createdRecord with { Data = data.ToJsonString() }, context);
            Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Rejected, result.Status, name);
            Assert.AreEqual(expectedReason, result.Reason, name);
            Assert.IsNull(result.Event, name);
        }
    }

    [TestMethod]
    public async Task RejectsUnknownVersionUnknownFieldsAndWrongTypedHorseReference()
    {
        using var fixture = await EventFixture.CreateAsync();
        var oldHorseId = HorseIdFor(8);
        var raceId = new RaceId("race-00000000-0000-4000-8000-000000000126");
        await fixture.CommandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2026, 10, 14), "TOKYO", 4, "形状確認"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new PublishRaceCardCommand(raceId, 12), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new RegisterEntryCommand(raceId, "entry-test", oldHorseId, 8), CancellationToken.None);
        var record = await fixture.ReadRecordAsync(raceId.Value, nameof(RaceAggregate), 3);
        var transformer = fixture.Transformer(new Dictionary<string, string> { [oldHorseId] = HorseIdFor(9) });

        var metadata = JsonNode.Parse(record.Metadata)!.AsObject();
        var versionName = FindPropertyName(metadata, "event_version");
        metadata[versionName] = 2;
        var unknownVersion = transformer.Transform(record with { Metadata = metadata.ToJsonString() });
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Rejected, unknownVersion.Status);
        Assert.IsNull(unknownVersion.Event);

        var unknownPayload = JsonNode.Parse(record.Data)!.AsObject();
        unknownPayload["unmodeledHorseReference"] = oldHorseId;
        var unknownField = transformer.Transform(record with { Data = unknownPayload.ToJsonString() });
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Rejected, unknownField.Status);
        Assert.IsNull(unknownField.Event);

        var wrongTypePayload = JsonNode.Parse(record.Data)!.AsObject();
        wrongTypePayload[FindPropertyName(wrongTypePayload, "horseId")] = 123;
        var wrongType = transformer.Transform(record with { Data = wrongTypePayload.ToJsonString() });
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Rejected, wrongType.Status);
        Assert.IsNull(wrongType.Event);
    }

    [TestMethod]
    public async Task EnforcesSerializedSizeAndJsonDepthBoundsBeforeReturningAnyRow()
    {
        using var fixture = await EventFixture.CreateAsync();
        var horseId = HorseIdFor(10);
        var raceId = new RaceId("race-00000000-0000-4000-8000-000000000127");
        await fixture.CommandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2026, 10, 14), "TOKYO", 5, "境界確認"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new PublishRaceCardCommand(raceId, 12), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new RegisterEntryCommand(raceId, "entry-test", horseId, 9), CancellationToken.None);
        var record = await fixture.ReadRecordAsync(raceId.Value, nameof(RaceAggregate), 3);
        var transformer = fixture.Transformer(new Dictionary<string, string> { [horseId] = HorseIdFor(11) });

        var oversized = transformer.Transform(record with { Data = new string('x', TypedHorseIdentityEventTransformer.MaximumSerializedFieldBytes + 1) });
        Assert.AreEqual(TypedHorseIdentityEventTransformReason.OversizedPayload, oversized.Reason);
        Assert.IsNull(oversized.Event);

        var nested = "0";
        for (var depth = 0; depth <= TypedHorseIdentityEventTransformer.MaximumJsonDepth; depth++)
            nested = $"{{\"x\":{nested}}}";
        var tooDeep = transformer.Transform(record with { Data = nested });
        Assert.AreEqual(TypedHorseIdentityEventTransformStatus.Rejected, tooDeep.Status);
        Assert.AreEqual(TypedHorseIdentityEventTransformReason.InvalidJson, tooDeep.Reason);
        Assert.IsNull(tooDeep.Event);

        var countingSerializer = System.Reflection.DispatchProxy.Create<IEventJsonSerializer, CountingEventJsonSerializer>();
        var countingProxy = (CountingEventJsonSerializer)countingSerializer;
        var preflightTransformer = fixture.Transformer(
            new Dictionary<string, string> { [horseId] = HorseIdFor(11) }, countingSerializer);
        var deepBeforeDeserialize = preflightTransformer.Transform(record with { Data = nested });
        Assert.AreEqual(TypedHorseIdentityEventTransformReason.InvalidJson, deepBeforeDeserialize.Reason);
        Assert.AreEqual(0, countingProxy.CallCount, "The bounded JSON parser must reject excess depth before EventFlow deserialization.");

        var duplicateProperty = record.Data.Insert(record.Data.IndexOf('{') + 1, "\"duplicate\":1,\"duplicate\":2,");
        var duplicateBeforeDeserialize = preflightTransformer.Transform(record with { Data = duplicateProperty });
        Assert.AreEqual(TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape, duplicateBeforeDeserialize.Reason);
        Assert.AreEqual(0, countingProxy.CallCount, "Duplicate JSON properties must be rejected before EventFlow deserialization.");

        var caseDuplicateProperty = record.Data.Insert(record.Data.IndexOf('{') + 1, "\"HorseId\":\"ignored\",");
        var caseDuplicateBeforeDeserialize = preflightTransformer.Transform(record with { Data = caseDuplicateProperty });
        Assert.AreEqual(TypedHorseIdentityEventTransformReason.UnsupportedPayloadShape, caseDuplicateBeforeDeserialize.Reason);
        Assert.AreEqual(0, countingProxy.CallCount, "Case-variant duplicate names must also be rejected before deserialization.");
    }

    [TestMethod]
    public async Task SnapshotsCallerMappingAndRejectsInvalidOrOversizedMappings()
    {
        using var fixture = await EventFixture.CreateAsync();
        var oldHorseId = HorseIdFor(12);
        var requestedNewHorseId = HorseIdFor(13);
        var laterNewHorseId = HorseIdFor(14);
        var raceId = new RaceId("race-00000000-0000-4000-8000-000000000128");
        await fixture.CommandBus.PublishAsync(
            new CreateRaceCommand(raceId, new DateOnly(2026, 10, 14), "TOKYO", 6, "写像固定"), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new PublishRaceCardCommand(raceId, 12), CancellationToken.None);
        await fixture.CommandBus.PublishAsync(new RegisterEntryCommand(raceId, "entry-test", oldHorseId, 10), CancellationToken.None);
        var record = await fixture.ReadRecordAsync(raceId.Value, nameof(RaceAggregate), 3);

        var callerMap = new Dictionary<string, string> { [oldHorseId] = requestedNewHorseId };
        var transformer = fixture.Transformer(callerMap);
        callerMap[oldHorseId] = laterNewHorseId;
        var result = transformer.Transform(record);
        Assert.IsNotNull(result.Event);
        Assert.AreEqual(requestedNewHorseId, StringProperty(JsonNode.Parse(result.Event.Data)!.AsObject(), "horseId"));

        var oversizedMap = Enumerable.Range(20, TypedHorseIdentityEventTransformer.MaximumMappingCount + 1)
            .ToDictionary(index => HorseIdFor(index), index => HorseIdFor(index + 100));
        Assert.Throws<ArgumentException>(() => fixture.Transformer(oversizedMap));
        var maximumMap = Enumerable.Range(20, TypedHorseIdentityEventTransformer.MaximumMappingCount)
            .ToDictionary(index => HorseIdFor(index), index => HorseIdFor(index + 100));
        _ = fixture.Transformer(maximumMap);
        Assert.Throws<ArgumentException>(() => fixture.Transformer(new Dictionary<string, string> { ["horse-not-a-guid"] = HorseIdFor(15) }));
    }

    private static void AssertMetadataPreservedExceptAggregateId(
        string originalJson,
        string transformedJson,
        string originalAggregateId,
        string newAggregateId)
    {
        var original = JsonNode.Parse(originalJson)!.AsObject();
        var transformed = JsonNode.Parse(transformedJson)!.AsObject();
        var aggregateIdKey = FindPropertyName(original, "aggregate_id");
        Assert.AreEqual(originalAggregateId, original[aggregateIdKey]!.GetValue<string>());
        Assert.AreEqual(newAggregateId, transformed[aggregateIdKey]!.GetValue<string>());
        original[aggregateIdKey] = newAggregateId;
        Assert.IsTrue(JsonNode.DeepEquals(original, transformed));
    }

    private static void AssertMetadataUnchanged(JsonNode original, JsonNode transformed) =>
        Assert.IsTrue(JsonNode.DeepEquals(original, transformed));

    private static string StringProperty(JsonObject value, string expectedName) =>
        value[FindPropertyName(value, expectedName)]!.GetValue<string>();

    private static JsonArray ArrayProperty(JsonObject value, string expectedName) =>
        value[FindPropertyName(value, expectedName)]!.AsArray();

    private static void AssertSubjectId(JsonObject value, string listName, int index, string expectedId)
    {
        var subject = ArrayProperty(value, listName)[index]!.AsObject();
        Assert.AreEqual(expectedId, StringProperty(subject, "subjectId"));
    }

    private static string FindPropertyName(JsonObject value, string expectedName) =>
        value.Select(property => property.Key)
            .SingleOrDefault(name => string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
        ?? throw new AssertFailedException($"Expected serialized field '{expectedName}' was not emitted by the registered EventFlow serializer.");

    private static string HorseIdFor(int value) => $"horse-{value:x8}-0000-4000-8000-000000000000";

    private static string DeterministicEntryId(string raceId, string horseId) =>
        HorseRacingPrediction.Contracts.Common.DeterministicIdGenerator.BuildRaceEntryId(raceId, horseId);

    private sealed class EventFixture : IDisposable
    {
        private readonly SqliteDbContextProvider _dbContextProvider;
        private readonly ServiceProvider _services;

        private EventFixture(SqliteDbContextProvider dbContextProvider, ServiceProvider services)
        {
            _dbContextProvider = dbContextProvider;
            _services = services;
            CommandBus = services.GetRequiredService<ICommandBus>();
            AggregateStore = services.GetRequiredService<EventFlow.Aggregates.IAggregateStore>();
        }

        public ICommandBus CommandBus { get; }
        public EventFlow.Aggregates.IAggregateStore AggregateStore { get; }

        public static async Task<EventFixture> CreateAsync()
        {
            var dbContextProvider = new SqliteDbContextProvider();
            await using (var context = dbContextProvider.CreateContext())
                await context.Database.EnsureCreatedAsync();

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            services.AddSingleton(dbContextProvider);
            services.AddSingleton<IDbContextProvider<EventStoreDbContext>>(dbContextProvider);
            services.AddEventFlow(options => options
                .ConfigureEntityFramework(EntityFrameworkConfiguration.New)
                .AddDefaults(typeof(RaceAggregate).Assembly)
                .AddDefaults(typeof(CreateRaceCommand).Assembly)
                .UseEntityFrameworkEventStore<EventStoreDbContext>());
            return new EventFixture(dbContextProvider, services.BuildServiceProvider());
        }

        public TypedHorseIdentityEventTransformer Transformer(
            IReadOnlyDictionary<string, string> map,
            IEventJsonSerializer? serializer = null) => new(
            serializer ?? _services.GetRequiredService<IEventJsonSerializer>(),
            _services.GetRequiredService<IDomainEventFactory>(),
            map);

        public IDomainEvent GetDomainEvent(TypedHorseIdentityEventRecord record) =>
            _services.GetRequiredService<IEventJsonSerializer>()
                .Deserialize(record.Data, record.Metadata);

        public IAggregateEvent GetAggregateEvent(TypedHorseIdentityEventRecord record) =>
            GetDomainEvent(record).GetAggregateEvent();

        public string EventType(TypedHorseIdentityEventRecord record) => GetAggregateEvent(record).GetType().Name;

        public async Task<TypedHorseIdentityEventRecord> ReadRecordAsync(string aggregateId, string aggregateName, int sequence)
        {
            await using var context = _dbContextProvider.CreateContext();
            var entity = await context.Set<EventEntity>().AsNoTracking().SingleAsync(row =>
                row.AggregateId == aggregateId && row.AggregateName == aggregateName
                && row.AggregateSequenceNumber == sequence);
            return new TypedHorseIdentityEventRecord(entity.GlobalSequenceNumber, entity.AggregateId,
                entity.AggregateName, entity.AggregateSequenceNumber, entity.Data, entity.Metadata);
        }

        public async Task<IReadOnlyList<TypedHorseIdentityEventRecord>> ReadRecordsAsync(
            string aggregateId, string aggregateName)
        {
            await using var context = _dbContextProvider.CreateContext();
            return await context.Set<EventEntity>().AsNoTracking().Where(row =>
                    row.AggregateId == aggregateId && row.AggregateName == aggregateName)
                .OrderBy(row => row.AggregateSequenceNumber)
                .Select(entity => new TypedHorseIdentityEventRecord(
                    entity.GlobalSequenceNumber, entity.AggregateId, entity.AggregateName,
                    entity.AggregateSequenceNumber, entity.Data, entity.Metadata))
                .ToListAsync();
        }

        public async Task<TypedHorseIdentityEventRecord> ReadRecordOfTypeAsync<TEvent>(string aggregateId)
            where TEvent : class, IAggregateEvent
        {
            await using var context = _dbContextProvider.CreateContext();
            var rows = await context.Set<EventEntity>().AsNoTracking().Where(row =>
                row.AggregateId == aggregateId && row.AggregateName == nameof(RaceAggregate))
                .OrderBy(row => row.AggregateSequenceNumber).ToListAsync();
            var serializer = _services.GetRequiredService<IEventJsonSerializer>();
            var entity = rows.Single(row => serializer.Deserialize(row.Data, row.Metadata)
                .GetAggregateEvent() is TEvent);
            return new TypedHorseIdentityEventRecord(entity.GlobalSequenceNumber, entity.AggregateId,
                entity.AggregateName, entity.AggregateSequenceNumber, entity.Data, entity.Metadata);
        }

        public async Task ReplaceRecordAsync(TypedHorseIdentityEventRecord record)
        {
            await using var context = _dbContextProvider.CreateContext();
            var rows = await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE EventEntity
                SET AggregateId = {record.AggregateId}, Data = {record.Data}, Metadata = {record.Metadata}
                WHERE GlobalSequenceNumber = {record.GlobalSequenceNumber}
                """);
            Assert.AreEqual(1, rows);
        }

        public void Dispose()
        {
            _services.Dispose();
            _dbContextProvider.Dispose();
        }
    }

    public class CountingEventJsonSerializer : System.Reflection.DispatchProxy
    {
        public int CallCount { get; private set; }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            CallCount++;
            throw new AssertFailedException("The serializer must not be invoked for invalid bounded JSON.");
        }
    }
}
