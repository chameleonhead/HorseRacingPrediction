using EventFlow.Queries;
using HorseRacingPrediction.Application.Queries.ReadModels;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HorseReadModel = HorseRacingPrediction.Application.Queries.ReadModels.HorseReadModel;
using JockeyReadModel = HorseRacingPrediction.Application.Queries.ReadModels.JockeyReadModel;
using TrainerReadModel = HorseRacingPrediction.Application.Queries.ReadModels.TrainerReadModel;

namespace HorseRacingPrediction.Api.Endpoints.Subjects;

internal static class SubjectCollectionEndpointMappings
{
    internal static string Normalize(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC), @"\s+", "");

    internal static async Task<SubjectCollectionPayload?> ResolveAsync(string kind, string id,
        IQueryProcessor queries, CancellationToken token)
    {
        var profile = await queries.ProcessAsync(new ReadModelByIdQuery<JraSubjectProfileReadModel>(id), token);
        var sourceIdentity = string.IsNullOrWhiteSpace(profile?.SourceIdentity) ? null : profile.SourceIdentity;
        if (kind == "Horse")
        {
            var horse = await queries.ProcessAsync(new ReadModelByIdQuery<HorseReadModel>(id), token);
            return horse is null || string.IsNullOrEmpty(horse.HorseId)
                ? null : new(id, kind, horse.RegisteredName, horse.BirthDate, sourceIdentity);
        }
        if (kind == "Trainer")
        {
            DateOnly? birth = DateOnly.TryParseExact(profile?.Fields.GetValueOrDefault("生年月日"),
                "yyyy年M月d日", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
            var trainer = await queries.ProcessAsync(new ReadModelByIdQuery<TrainerReadModel>(id), token);
            return trainer is null || string.IsNullOrEmpty(trainer.TrainerId)
                ? null : new(id, kind, trainer.DisplayName, birth, sourceIdentity);
        }
        if (kind == "Jockey")
        {
            DateOnly? birth = DateOnly.TryParseExact(profile?.Fields.GetValueOrDefault("生年月日"),
                "yyyy年M月d日", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
            var jockey = await queries.ProcessAsync(new ReadModelByIdQuery<JockeyReadModel>(id), token);
            return jockey is null || string.IsNullOrEmpty(jockey.JockeyId)
                ? null : new(id, kind, jockey.DisplayName, birth, sourceIdentity);
        }
        return null;
    }

    internal sealed record SubjectCollectionPayload(string SubjectId, string SubjectType, string Name,
        DateOnly? BirthDate, string? SourceIdentity);
}
