using System.Reflection;
using HorseRacingPrediction.Application.Queries.ReadModels;

namespace HorseRacingPrediction.AgentClient.Http;

/// <summary>
/// HTTP レスポンスの DTO から EventFlow ReadModel インスタンスを構築するヘルパー。
/// ReadModel は private setter を持つため、リフレクションでプロパティとフィールドを設定する。
/// </summary>
internal static class ReadModelMapper
{
    // ------------------------------------------------------------------ //
    // RacePredictionContextReadModel
    // ------------------------------------------------------------------ //

    public static RacePredictionContextReadModel? ToRacePredictionContext(RacePredictionContextDto? dto)
    {
        if (dto is null || string.IsNullOrEmpty(dto.RaceId))
            return null;

        var model = new RacePredictionContextReadModel();
        SetProp(model, nameof(RacePredictionContextReadModel.RaceId), dto.RaceId);
        SetProp(model, nameof(RacePredictionContextReadModel.RaceDate), dto.RaceDate);
        SetProp(model, nameof(RacePredictionContextReadModel.RacecourseCode), dto.RacecourseCode);
        SetProp(model, nameof(RacePredictionContextReadModel.RaceNumber), dto.RaceNumber);
        SetProp(model, nameof(RacePredictionContextReadModel.RaceName), dto.RaceName);
        SetProp(model, nameof(RacePredictionContextReadModel.Status), dto.Status);
        SetProp(model, nameof(RacePredictionContextReadModel.GradeCode), dto.GradeCode);
        SetProp(model, nameof(RacePredictionContextReadModel.SurfaceCode), dto.SurfaceCode);
        SetProp(model, nameof(RacePredictionContextReadModel.DistanceMeters), dto.DistanceMeters);
        SetProp(model, nameof(RacePredictionContextReadModel.DirectionCode), dto.DirectionCode);
        AddToList<RacePredictionContextReadModel, RacePredictionContextEntry>(model, "_entries", dto.Entries);
        AddToList<RacePredictionContextReadModel, WeatherObservationSnapshot>(model, "_weatherObservations", dto.WeatherObservations);
        AddToList<RacePredictionContextReadModel, TrackConditionSnapshot>(model, "_trackConditionObservations", dto.TrackConditionObservations);
        return model;
    }

    // ------------------------------------------------------------------ //
    // HorseReadModel
    // ------------------------------------------------------------------ //

    public static HorseReadModel? ToHorse(HorseDto? dto)
    {
        if (dto is null || string.IsNullOrEmpty(dto.HorseId))
            return null;

        var model = new HorseReadModel();
        SetProp(model, nameof(HorseReadModel.HorseId), dto.HorseId);
        SetProp(model, nameof(HorseReadModel.RegisteredName), dto.RegisteredName);
        SetProp(model, nameof(HorseReadModel.NormalizedName), dto.NormalizedName);
        SetProp(model, nameof(HorseReadModel.SexCode), dto.SexCode);
        SetProp(model, nameof(HorseReadModel.BirthDate), dto.BirthDate);
        AddToList<HorseReadModel, HorseAliasEntry>(model, "_aliases", dto.Aliases);
        return model;
    }

    // ------------------------------------------------------------------ //
    // JockeyReadModel
    // ------------------------------------------------------------------ //

    public static JockeyReadModel? ToJockey(JockeyDto? dto)
    {
        if (dto is null || string.IsNullOrEmpty(dto.JockeyId))
            return null;

        var model = new JockeyReadModel();
        SetProp(model, nameof(JockeyReadModel.JockeyId), dto.JockeyId);
        SetProp(model, nameof(JockeyReadModel.DisplayName), dto.DisplayName);
        SetProp(model, nameof(JockeyReadModel.NormalizedName), dto.NormalizedName);
        SetProp(model, nameof(JockeyReadModel.AffiliationCode), dto.AffiliationCode);
        AddToList<JockeyReadModel, JockeyAliasEntry>(model, "_aliases", dto.Aliases);
        return model;
    }

    // ------------------------------------------------------------------ //
    // MemoBySubjectReadModel
    // ------------------------------------------------------------------ //

    public static MemoBySubjectReadModel? ToMemoBySubject(string subjectKey, List<MemoResponseDto>? memos)
    {
        if (memos is null || memos.Count == 0)
            return null;

        var model = new MemoBySubjectReadModel();
        SetProp(model, nameof(MemoBySubjectReadModel.SubjectKey), subjectKey);
        var snapshots = memos.Select(m => new MemoSnapshot(
            m.MemoId,
            m.AuthorId,
            m.MemoType,
            m.Content,
            m.CreatedAt,
            m.Subjects.Select(s => new MemoSubjectSnapshot(s.SubjectType, s.SubjectId)).ToList(),
            m.Links.Select(l => new MemoLinkSnapshot(l.LinkId, l.LinkType, l.Title, l.Url, l.StorageKey)).ToList()))
            .ToList();
        AddToList<MemoBySubjectReadModel, MemoSnapshot>(model, "_memos", snapshots);
        return model;
    }

    // ------------------------------------------------------------------ //
    // HorseRaceHistoryReadModel
    // ------------------------------------------------------------------ //

    public static HorseRaceHistoryReadModel? ToHorseRaceHistory(HorseRaceHistoryDto? dto)
    {
        if (dto is null || string.IsNullOrEmpty(dto.HorseId))
            return null;

        var model = new HorseRaceHistoryReadModel();
        SetProp(model, nameof(HorseRaceHistoryReadModel.HorseId), dto.HorseId);
        AddToList<HorseRaceHistoryReadModel, HorseRaceHistoryEntry>(model, "_entries", dto.Entries);
        return model;
    }

    // ------------------------------------------------------------------ //
    // JockeyRaceHistoryReadModel
    // ------------------------------------------------------------------ //

    public static JockeyRaceHistoryReadModel? ToJockeyRaceHistory(JockeyRaceHistoryDto? dto)
    {
        if (dto is null || string.IsNullOrEmpty(dto.JockeyId))
            return null;

        var model = new JockeyRaceHistoryReadModel();
        SetProp(model, nameof(JockeyRaceHistoryReadModel.JockeyId), dto.JockeyId);
        AddToList<JockeyRaceHistoryReadModel, JockeyRaceHistoryEntry>(model, "_entries", dto.Entries);
        return model;
    }

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private static void SetProp<T>(T obj, string propertyName, object? value)
    {
        var prop = typeof(T)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"{typeof(T).Name} にプロパティ '{propertyName}' が見つかりません。ReadModel の構造変更を確認してください。");
        prop.SetValue(obj, value);
    }

    private static void AddToList<TModel, TItem>(TModel obj, string fieldName, IEnumerable<TItem>? items)
    {
        if (items is null)
            return;

        var field = typeof(TModel).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"{typeof(TModel).Name} にフィールド '{fieldName}' が見つかりません。ReadModel の構造変更を確認してください。");
        var list = field.GetValue(obj) as List<TItem>
            ?? throw new InvalidOperationException(
                $"{typeof(TModel).Name}.{fieldName} を List<{typeof(TItem).Name}> として取得できません。");
        list.AddRange(items);
    }
}

// ------------------------------------------------------------------ //
// DTO classes used for HTTP deserialization
// ------------------------------------------------------------------ //

internal sealed class RacePredictionContextDto
{
    public string RaceId { get; set; } = string.Empty;
    public DateOnly? RaceDate { get; set; }
    public string? RacecourseCode { get; set; }
    public int? RaceNumber { get; set; }
    public string? RaceName { get; set; }
    public HorseRacingPrediction.Domain.Races.RaceStatus Status { get; set; }
    public string? GradeCode { get; set; }
    public string? SurfaceCode { get; set; }
    public int? DistanceMeters { get; set; }
    public string? DirectionCode { get; set; }
    public List<RacePredictionContextEntry> Entries { get; set; } = new();
    public List<WeatherObservationSnapshot> WeatherObservations { get; set; } = new();
    public List<TrackConditionSnapshot> TrackConditionObservations { get; set; } = new();
}

internal sealed class HorseDto
{
    public string HorseId { get; set; } = string.Empty;
    public string RegisteredName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? SexCode { get; set; }
    public DateOnly? BirthDate { get; set; }
    public List<HorseAliasEntry> Aliases { get; set; } = new();
}

internal sealed class JockeyDto
{
    public string JockeyId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? AffiliationCode { get; set; }
    public List<JockeyAliasEntry> Aliases { get; set; } = new();
}

internal sealed class MemoResponseDto
{
    public string MemoId { get; set; } = string.Empty;
    public string? AuthorId { get; set; }
    public string MemoType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public List<MemoSubjectDto> Subjects { get; set; } = new();
    public List<MemoLinkDto> Links { get; set; } = new();
}

internal sealed class MemoSubjectDto
{
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
}

internal sealed class MemoLinkDto
{
    public string LinkId { get; set; } = string.Empty;
    public string LinkType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? StorageKey { get; set; }
}

internal sealed class HorseRaceHistoryDto
{
    public string HorseId { get; set; } = string.Empty;
    public List<HorseRaceHistoryEntry> Entries { get; set; } = new();
}

internal sealed class JockeyRaceHistoryDto
{
    public string JockeyId { get; set; } = string.Empty;
    public List<JockeyRaceHistoryEntry> Entries { get; set; } = new();
}
