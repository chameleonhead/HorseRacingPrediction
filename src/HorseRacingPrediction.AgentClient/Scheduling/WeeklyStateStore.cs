using System.Text.Json;
using System.Text.Json.Serialization;
using HorseRacingPrediction.Agents.Workflow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HorseRacingPrediction.AgentClient.Scheduling;

/// <summary>
/// フェーズ間で <see cref="WeekendRaceInfo"/> リストを永続化するストア。
/// <para>
/// 状態は JSON ファイルに保存する。ファイル名は第1開催日の日付で決まる。
/// アプリ再起動後も状態が復元されるため、発見フェーズで取得したレース情報を後続フェーズで再利用できる。
/// </para>
/// </summary>
public sealed class WeeklyStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _stateDirectory;
    private readonly ILogger<WeeklyStateStore> _logger;

    public WeeklyStateStore(IOptions<WeeklySchedulerOptions> options, ILogger<WeeklyStateStore> logger)
    {
        var dir = options.Value.StateDirectory;
        _stateDirectory = string.IsNullOrWhiteSpace(dir)
            ? Path.Combine(AppContext.BaseDirectory, "scheduler-state")
            : dir;
        _logger = logger;
    }

    /// <summary>指定した第1開催日のレース発見結果をファイルへ保存する。</summary>
    public async Task SaveRacesAsync(
        DateOnly firstRaceDay,
        IReadOnlyList<WeekendRaceInfo> races,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_stateDirectory);
        var filePath = GetFilePath(firstRaceDay);

        var dto = new WeeklyStateDto
        {
            FirstRaceDay = firstRaceDay,
            Races = races.Select(r => new WeekendRaceInfoDto(r)).ToList()
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("状態を保存しました: {FilePath} ({Count} レース)", filePath, races.Count);
    }

    /// <summary>指定した第1開催日のレース発見結果をファイルから読み込む。存在しない場合は null を返す。</summary>
    public async Task<IReadOnlyList<WeekendRaceInfo>?> LoadRacesAsync(
        DateOnly firstRaceDay,
        CancellationToken cancellationToken = default)
    {
        var filePath = GetFilePath(firstRaceDay);
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("状態ファイルが見つかりません: {FilePath}", filePath);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<WeeklyStateDto>(json, JsonOptions);
            if (dto is null)
                return null;

            var races = dto.Races.Select(r => r.ToWeekendRaceInfo()).ToList();
            _logger.LogInformation("状態を読み込みました: {FilePath} ({Count} レース)", filePath, races.Count);
            return races;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "状態ファイルの読み込みに失敗しました: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>指定した第1開催日の状態ファイルを削除する（次サイクルへの引き継ぎ防止）。</summary>
    public void DeleteState(DateOnly firstRaceDay)
    {
        var filePath = GetFilePath(firstRaceDay);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("状態ファイルを削除しました: {FilePath}", filePath);
        }
    }

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private string GetFilePath(DateOnly firstRaceDay) =>
        Path.Combine(_stateDirectory, $"races-{firstRaceDay:yyyy-MM-dd}.json");

    // ------------------------------------------------------------------ //
    // DTOs for serialization
    // ------------------------------------------------------------------ //

    private sealed class WeeklyStateDto
    {
        public DateOnly FirstRaceDay { get; set; }
        public List<WeekendRaceInfoDto> Races { get; set; } = new();
    }

    private sealed class WeekendRaceInfoDto
    {
        public WeekendRaceInfoDto() { }

        public WeekendRaceInfoDto(WeekendRaceInfo r)
        {
            RaceName = r.RaceName;
            RaceDate = r.RaceDate;
            Racecourse = r.Racecourse;
            RaceNumber = r.RaceNumber;
            RaceQuery = r.RaceQuery;
            HorseNames = r.HorseNames.ToList();
            JockeyNames = r.JockeyNames.ToList();
            TrainerNames = r.TrainerNames.ToList();
        }

        public string RaceName { get; set; } = string.Empty;
        public DateOnly RaceDate { get; set; }
        public string Racecourse { get; set; } = string.Empty;
        public int RaceNumber { get; set; }
        public string RaceQuery { get; set; } = string.Empty;
        public List<string> HorseNames { get; set; } = new();
        public List<string> JockeyNames { get; set; } = new();
        public List<string> TrainerNames { get; set; } = new();

        public WeekendRaceInfo ToWeekendRaceInfo() =>
            new(RaceName, RaceDate, Racecourse, RaceNumber, RaceQuery, HorseNames, JockeyNames, TrainerNames);
    }
}
