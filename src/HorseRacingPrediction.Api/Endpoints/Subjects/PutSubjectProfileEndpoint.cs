using EventFlow;
using EventFlow.Commands;
using EventFlow.Queries;
using HorseRacingPrediction.Api.Security;
using HorseRacingPrediction.Application.Commands.Horses;
using HorseRacingPrediction.Application.Commands.Jockeys;
using HorseRacingPrediction.Application.Commands.Trainers;
using Microsoft.AspNetCore.Mvc;
using HorseRacingPrediction.Domain;
using HorseRacingPrediction.Domain.Horses;
using HorseRacingPrediction.Domain.Jockeys;
using HorseRacingPrediction.Domain.Trainers;
using System.Globalization;
using System.Text.RegularExpressions;

using HorseRacingPrediction.Contracts.Common;
using HorseRacingPrediction.Contracts.Subjects;

namespace HorseRacingPrediction.Api.Endpoints.Subjects;

internal static class PutSubjectProfileEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/v2/admin/subjects/{kind}/{subjectId}/profile",
            async (string kind, string subjectId, PutSubjectProfileRequest request, [FromServices] IQueryProcessor queries,
                [FromServices] ICommandBus commands, CancellationToken token) =>
            {
                var subject = await SubjectCollectionEndpointMappings.ResolveAsync(kind, subjectId, queries, token);
                if (subject is null) return Results.NotFound();
                var profile = request.Profile;
                if (profile?.Fields is null || profile.SubjectType != subject.SubjectType
                    || JraSubjectNameNormalizer.NormalizeIdentityName(profile.SubjectType, profile.Name)
                        != JraSubjectNameNormalizer.NormalizeIdentityName(subject.SubjectType, subject.Name)
                    || !Uri.TryCreate(profile.SourceUrl, UriKind.Absolute, out var source)
                    || source.Scheme != "https" || source.Host != "www.jra.go.jp"
                    || string.IsNullOrWhiteSpace(profile.SourceIdentity)
                    || kind == "Horse" && !JraSourceIdentity.MatchesHorse(profile.SourceIdentity, profile.SourceUrl))
                    return Results.BadRequest(new[] { "プロフィールの識別情報が不正です。" });
                if (!DateOnly.TryParseExact(profile.Fields.GetValueOrDefault("生年月日"), "yyyy年M月d日",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth)
                    || subject.BirthDate is not null && birth != subject.BirthDate
                    || subject.SourceIdentity is not null && (kind == "Horse"
                        ? !JraSourceIdentity.MatchesHorse(subject.SourceIdentity, profile.SourceIdentity)
                        : subject.SourceIdentity != profile.SourceIdentity))
                    return Results.Conflict(new[] { "同定不能: 保存済みの対象とプロフィールが一致しません。" });

                var data = new CollectedSubjectProfile(profile.Name, profile.SourceIdentity,
                    profile.SourceUrl, profile.Fields, profile.AcquiredAt);
                if (kind == "Horse")
                {
                    await commands.PublishAsync(new CollectHorseProfileCommand(new HorseId(subjectId), data), token);
                    static string? Field(IReadOnlyDictionary<string, string> fields, params string[] names) =>
                        names.Select(fields.GetValueOrDefault).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                    var dam = Field(profile.Fields, "母", "母馬");
                    var damsire = dam is null ? null
                        : Regex.Match(dam, @"母の父\s*[:：]\s*(?<value>[^\)）]+)").Groups["value"].Value.Trim();
                    if (dam is not null) dam = Regex.Replace(dam, @"[\(（]母の父：.*$", string.Empty).Trim();
                    await commands.PublishAsync(new UpdateHorseProfileCommand(new HorseId(subjectId),
                        ownerName: Field(profile.Fields, "馬主", "馬主名"),
                        breederName: Field(profile.Fields, "生産者", "生産牧場"),
                        sireName: Field(profile.Fields, "父", "父馬"), damName: dam,
                        damsireName: string.IsNullOrWhiteSpace(damsire) ? null : damsire,
                        coatColor: Field(profile.Fields, "毛色")), token);
                }
                else if (kind == "Trainer")
                    await commands.PublishAsync(new CollectTrainerProfileCommand(new TrainerId(subjectId), data), token);
                else
                    await commands.PublishAsync(new UpdateJockeyProfileCommand(new JockeyId(subjectId),
                        displayName: profile.Name, normalizedName: SubjectCollectionEndpointMappings.Normalize(profile.Name),
                        affiliationCode: profile.Fields.GetValueOrDefault("所属")), token);
                return Results.Ok();
            })
            .AddEndpointFilter<RaceWriteEndpointFilter>();
    }
}
