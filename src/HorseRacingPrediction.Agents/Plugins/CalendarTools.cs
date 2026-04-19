using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.AI;

namespace HorseRacingPrediction.Agents.Plugins;

/// <summary>
/// 現在日時・曜日計算・競馬場コード変換など、
/// 日付やカレンダーに関するユーティリティツールを提供する
/// Microsoft Agent Framework プラグイン。
/// <see cref="GetAITools"/> で <see cref="AITool"/> 一覧を取得し、
/// <see cref="Microsoft.Agents.AI.ChatClientAgent"/> に渡すことで利用可能になる。
/// </summary>
public sealed class CalendarTools
{
    private static readonly IReadOnlyDictionary<string, string> RacecourseCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["札幌"] = "01",
            ["函館"] = "02",
            ["福島"] = "03",
            ["新潟"] = "04",
            ["東京"] = "05",
            ["中山"] = "06",
            ["中京"] = "07",
            ["京都"] = "08",
            ["阪神"] = "09",
            ["小倉"] = "10",
        };

    private static readonly CultureInfo JapaneseCulture = new("ja-JP");

    private readonly TimeProvider _timeProvider;

    public CalendarTools(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 現在の日本時間の日時を返す。
    /// </summary>
    [Description("現在の日本時間（JST）の日時を取得します。年月日・曜日・時分を返します。")]
    public string GetCurrentDateTime()
    {
        var jst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var now = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), jst);
        var dayOfWeek = now.ToString("dddd", JapaneseCulture);
        return $"{now:yyyy年M月d日}（{dayOfWeek}）{now:H時m分}";
    }

    /// <summary>
    /// 指定日から最も近い次の週末（土曜・日曜）の日付を返す。
    /// 土曜・日曜に呼び出した場合はその週末を返す。
    /// </summary>
    [Description("指定日（省略時は今日）から最も近い次の週末（土・日）の日付を返します。")]
    public string GetWeekendDates(
        [Description("基準日（yyyy-MM-dd 形式、省略時は今日）")] string? baseDate = null)
    {
        DateOnly date;
        if (string.IsNullOrWhiteSpace(baseDate))
        {
            var jst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
            var now = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), jst);
            date = DateOnly.FromDateTime(now.DateTime);
        }
        else
        {
            date = DateOnly.ParseExact(baseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var saturday = GetNextSaturday(date);
        var sunday = saturday.AddDays(1);

        return $"土曜日: {saturday:yyyy-MM-dd}（{saturday:yyyy年M月d日}）\n" +
               $"日曜日: {sunday:yyyy-MM-dd}（{sunday:yyyy年M月d日}）";
    }

    /// <summary>
    /// 競馬場名から JRA の競馬場コードを返す。
    /// </summary>
    [Description("競馬場名（例: 東京、中山）から JRA 競馬場コードを取得します。")]
    public string GetJraRacecourseCode(
        [Description("競馬場名（日本語。例: 東京、中山、阪神）")] string racecourseName)
    {
        if (RacecourseCodes.TryGetValue(racecourseName.Trim(), out var code))
        {
            return $"{racecourseName}: {code}";
        }

        var available = string.Join("、", RacecourseCodes.Keys);
        return $"競馬場名 '{racecourseName}' は見つかりませんでした。利用可能な競馬場: {available}";
    }

    /// <summary>
    /// このプラグインのメソッドを <see cref="AITool"/> 一覧として返す。
    /// </summary>
    public IList<AITool> GetAITools() =>
    [
        AIFunctionFactory.Create(GetCurrentDateTime),
        AIFunctionFactory.Create(GetWeekendDates),
        AIFunctionFactory.Create(GetJraRacecourseCode)
    ];

    // ------------------------------------------------------------------ //
    // private helpers
    // ------------------------------------------------------------------ //

    private static DateOnly GetNextSaturday(DateOnly date)
    {
        return date.DayOfWeek switch
        {
            DayOfWeek.Saturday => date,
            DayOfWeek.Sunday => date.AddDays(-1),
            _ => date.AddDays(((int)DayOfWeek.Saturday - (int)date.DayOfWeek + 7) % 7)
        };
    }
}
