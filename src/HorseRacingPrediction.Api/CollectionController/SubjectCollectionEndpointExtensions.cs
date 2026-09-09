using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EventFlow;
using EventFlow.Queries;
using EventFlow.EntityFramework;
using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Trainers;
using HorseRacingPrediction.Application.Commands.Races;
using HorseRacingPrediction.Application.Queries.ReadModels;
using HorseRacingPrediction.Collector.Scheduling;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.Domain;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Trainers;
using HorseRacingPrediction.Domain.Races;
using HorseRacingPrediction.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using HorseReadModel = HorseRacingPrediction.Application.Queries.ReadModels.HorseReadModel;
using TrainerReadModel = HorseRacingPrediction.Application.Queries.ReadModels.TrainerReadModel;

namespace HorseRacingPrediction.Api.CollectionController;

public static class SubjectCollectionEndpointExtensions
{
    public static IEndpointRouteBuilder MapSubjectCollectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/subjects/{kind}/{subjectId}");
        group.MapGet("/profile", async (string kind, string subjectId, IQueryProcessor queries, CancellationToken token) =>
        {
            if (await ResolveAsync(kind, subjectId, queries, token) is null) return Results.NotFound();
            var profile = await queries.ProcessAsync(new ReadModelByIdQuery<JraSubjectProfileReadModel>(subjectId), token);
            return profile is null || string.IsNullOrEmpty(profile.SubjectId) ? Results.NoContent() : Results.Ok(new JraSubjectProfileDto(kind,
                profile.Name, profile.SourceIdentity, profile.SourceUrl, profile.Fields, profile.AcquiredAt));
        });
        group.MapGet("/collection/{operation}", async (string kind, string subjectId, string operation, IQueryProcessor queries, ProcessingStateStore store, CancellationToken token) =>
        {
            var jobType = JobType(kind, operation);
            if (jobType is null) return Results.BadRequest();
            if (await ResolveAsync(kind, subjectId, queries, token) is null) return Results.NotFound();
            var status = await store.GetSubjectCollectionAsync(jobType, subjectId, token);
            return status is null ? Results.NoContent() : Results.Ok(status);
        });
        group.MapPost("/collection/{operation}", async (string kind, string subjectId, string operation, IQueryProcessor queries, ProcessingStateStore store, HttpContext context, CancellationToken token) =>
        {
            var jobType = JobType(kind, operation);
            if (jobType is null) return Results.BadRequest();
            var subject = await ResolveAsync(kind, subjectId, queries, token);
            if (subject is null) return Results.NotFound();
            var id = await store.RequestSubjectCollectionAsync(jobType, subject, context.User.Identity?.Name ?? "Admin API", DateTimeOffset.UtcNow, token);
            return Results.Accepted($"/api/admin/jobs/{Uri.EscapeDataString(id)}", new { jobId = id });
        });
        group.MapPost("/collection/{operation}/retry", async (string kind, string subjectId, string operation, IQueryProcessor queries, ProcessingStateStore store, HttpContext context, CancellationToken token) =>
        {
            var type = JobType(kind, operation);
            if (type is null) return Results.BadRequest();
            if (await ResolveAsync(kind, subjectId, queries, token) is null) return Results.NotFound();
            var status = await store.GetSubjectCollectionAsync(type, subjectId, token);
            if (status is null) return Results.NotFound();
            if (status.Job.Status is not (AgentJobStatus.Failed or AgentJobStatus.DeadLetter)) return Results.Conflict();
            await store.RerunJobAsync(status.Job.JobId, status.Job.UpdatedAt, context.User.Identity?.Name ?? "Admin API",
                "失敗分を再試行", DateTimeOffset.UtcNow, token);
            return Results.Accepted();
        });
        group.MapPost("/profile", async (string kind, string subjectId, JraSubjectProfileDto request, IQueryProcessor queries, ICommandBus commands, CancellationToken token) =>
        {
            var subject = await ResolveAsync(kind, subjectId, queries, token);
            if (subject is null) return Results.NotFound();
            if (request.Fields is null || request.SubjectType != subject.SubjectType || Normalize(request.Name) != Normalize(subject.Name)
                || !Uri.TryCreate(request.SourceUrl, UriKind.Absolute, out var source) || source.Scheme != "https" || source.Host != "www.jra.go.jp"
                || string.IsNullOrWhiteSpace(request.SourceIdentity)) return Results.BadRequest(new[] { "プロフィールの識別情報が不正です。" });
            if (!DateOnly.TryParseExact(request.Fields.GetValueOrDefault("生年月日"), "yyyy年M月d日", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth)
                || (subject.BirthDate is not null && birth != subject.BirthDate)
                || (subject.SourceIdentity is not null && subject.SourceIdentity != request.SourceIdentity))
                return Results.Conflict(new[] { "同定不能: 保存済みの対象とプロフィールが一致しません。" });
            var data = new CollectedSubjectProfile(request.Name, request.SourceIdentity, request.SourceUrl, request.Fields, request.AcquiredAt);
            if (kind == "Horse")
            {
                await commands.PublishAsync(new CollectHorseProfileCommand(new HorseId(subjectId), data), token);
                static string? Field(IReadOnlyDictionary<string, string> fields, params string[] names) =>
                    names.Select(fields.GetValueOrDefault).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var dam = Field(request.Fields, "母", "母馬");
                var damsire = dam is null ? null : Regex.Match(dam, @"母の父\s*[:：]\s*(?<value>[^\)）]+)").Groups["value"].Value.Trim();
                if (dam is not null) dam = Regex.Replace(dam, @"[\(（]母の父：.*$", string.Empty).Trim();
                await commands.PublishAsync(new UpdateHorseProfileCommand(new HorseId(subjectId),
                    ownerName: Field(request.Fields, "馬主", "馬主名"),
                    breederName: Field(request.Fields, "生産者", "生産牧場"),
                    sireName: Field(request.Fields, "父", "父馬"), damName: dam,
                    damsireName: string.IsNullOrWhiteSpace(damsire) ? null : damsire,
                    coatColor: Field(request.Fields, "毛色")), token);
            }
            else await commands.PublishAsync(new CollectTrainerProfileCommand(new TrainerId(subjectId), data), token);
            return Results.Ok();
        });
        endpoints.MapPost("/api/admin/collection/horse-history/race", async (PrepareHorseHistoryRaceRequest request,
            IDbContextProvider<EventStoreDbContext> provider, ICommandBus commands, CancellationToken token) =>
        {
            var course = RaceReacquisitionEndpointExtensions.ResolveCourse(request.Course);
            if (course is null || request.RaceNumber is < 1 or > 12 || string.IsNullOrWhiteSpace(request.RaceName)) return Results.BadRequest();
            using var db = provider.CreateContext();
            var candidates = await db.RacePredictionContexts.AsNoTracking().Where(x => x.RaceDate == request.RaceDate && x.RaceNumber == request.RaceNumber).ToListAsync(token);
            var matches = candidates.Where(x => RaceReacquisitionEndpointExtensions.ResolveCourse(x.RacecourseCode) == course).ToArray();
            if (matches.Length > 1) return Results.Conflict(new[] { "同一日・競馬場・レース番号の登録が複数あります。" });
            var id = matches.FirstOrDefault()?.RaceId ?? DeterministicIdGenerator.BuildRaceId(request.RaceDate, course, request.RaceNumber);
            if (matches.Length == 0)
            {
                try { await commands.PublishAsync(new CreateRaceCommand(new RaceId(id), request.RaceDate, course, request.RaceNumber, request.RaceName), token); }
                catch (InvalidOperationException ex) when (ex.Message == "Race is already created.") { }
            }
            return Results.Ok(new { raceId = id });
        });
        return endpoints;
    }
    private static string? JobType(string kind, string operation) => kind is not ("Horse" or "Trainer") ? null : operation switch
    { "profile" => AgentJobType.SubjectProfileRefresh, "history" when kind == "Horse" => AgentJobType.HorseHistoryDiscovery, _ => null };
    private static string Normalize(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC), @"\s+", "");
    private static async Task<SubjectCollectionPayload?> ResolveAsync(string kind, string id, IQueryProcessor queries, CancellationToken token)
    {
        var profile = await queries.ProcessAsync(new ReadModelByIdQuery<JraSubjectProfileReadModel>(id), token);
        var sourceIdentity = string.IsNullOrWhiteSpace(profile?.SourceIdentity) ? null : profile.SourceIdentity;
        if (kind == "Horse")
        {
            var horse = await queries.ProcessAsync(new ReadModelByIdQuery<HorseReadModel>(id), token);
            return horse is null || string.IsNullOrEmpty(horse.HorseId) ? null : new(id, kind, horse.RegisteredName, horse.BirthDate, sourceIdentity);
        }
        if (kind == "Trainer")
        {
            var trainer = await queries.ProcessAsync(new ReadModelByIdQuery<TrainerReadModel>(id), token);
            DateOnly? birth = DateOnly.TryParseExact(profile?.Fields.GetValueOrDefault("生年月日"), "yyyy年M月d日", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            return trainer is null || string.IsNullOrEmpty(trainer.TrainerId) ? null : new(id, kind, trainer.DisplayName, birth, sourceIdentity);
        }
        return null;
    }
}
