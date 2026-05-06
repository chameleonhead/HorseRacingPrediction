using System.Globalization;
using System.Text.RegularExpressions;

namespace HorseRacingPrediction.Agents.Agents;

/// <summary>
/// ページ整形・分析時に使う入力サイズ制御プロファイル。
/// </summary>
public sealed record PageExtractionProfile(
    string Name,
    int MaxInputLength,
    int MaxPromptLinks,
    int MaxSearchResultLinks,
    bool IncludeSnapshotInPrompt,
    int SnapshotTextLength,
    int SnapshotLinks,
    int SnapshotActions,
    int SnapshotTables,
    int SnapshotRows)
{
    public static PageExtractionProfile Standard { get; } = new(
        Name: "standard",
        MaxInputLength: 12_000,
        MaxPromptLinks: 20,
        MaxSearchResultLinks: 12,
        IncludeSnapshotInPrompt: true,
        SnapshotTextLength: 4_000,
        SnapshotLinks: 50,
        SnapshotActions: 30,
        SnapshotTables: 5,
        SnapshotRows: 10);

    public static PageExtractionProfile Small { get; } = new(
        Name: "small",
        MaxInputLength: 6_000,
        MaxPromptLinks: 12,
        MaxSearchResultLinks: 10,
        IncludeSnapshotInPrompt: true,
        SnapshotTextLength: 1_500,
        SnapshotLinks: 15,
        SnapshotActions: 10,
        SnapshotTables: 2,
        SnapshotRows: 5);

    public static PageExtractionProfile Tiny { get; } = new(
        Name: "tiny",
        MaxInputLength: 3_000,
        MaxPromptLinks: 8,
        MaxSearchResultLinks: 8,
        IncludeSnapshotInPrompt: false,
        SnapshotTextLength: 0,
        SnapshotLinks: 0,
        SnapshotActions: 0,
        SnapshotTables: 0,
        SnapshotRows: 0);
}

/// <summary>
/// モデル名と実行時設定からページ整形プロファイルを解決する。
/// </summary>
public static class PageExtractionProfileResolver
{
    private static readonly Regex BillionsPattern = new(@"(?<size>\d+(?:\.\d+)?)b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static PageExtractionProfile Resolve(string? modelId = null, string? profileOverride = null)
    {
        var resolvedModel = modelId ??
            Environment.GetEnvironmentVariable("LLM_MODEL") ??
            Environment.GetEnvironmentVariable("LMSTUDIO_MODEL") ??
            Environment.GetEnvironmentVariable("OPENAI_MODEL");

        var overrideName = profileOverride ?? Environment.GetEnvironmentVariable("PAGE_EXTRACTION_PROFILE");
        var profile = ResolveBaseProfile(resolvedModel, overrideName);
        return ApplyNumericOverrides(profile);
    }

    private static PageExtractionProfile ResolveBaseProfile(string? modelId, string? overrideName)
    {
        if (!string.IsNullOrWhiteSpace(overrideName))
        {
            var normalized = overrideName.Trim().ToLowerInvariant();
            return normalized switch
            {
                "tiny" => PageExtractionProfile.Tiny,
                "small" => PageExtractionProfile.Small,
                "standard" => PageExtractionProfile.Standard,
                "default" => PageExtractionProfile.Standard,
                _ => PageExtractionProfile.Standard,
            };
        }

        var normalizedModel = (modelId ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedModel))
        {
            return PageExtractionProfile.Standard;
        }

        if (normalizedModel.Contains("mini", StringComparison.Ordinal) ||
            normalizedModel.Contains("small", StringComparison.Ordinal) ||
            normalizedModel.Contains("1.5b", StringComparison.Ordinal) ||
            normalizedModel.Contains("2b", StringComparison.Ordinal) ||
            normalizedModel.Contains("3b", StringComparison.Ordinal) ||
            normalizedModel.Contains("e2b", StringComparison.Ordinal))
        {
            return PageExtractionProfile.Tiny;
        }

        var billionMatch = BillionsPattern.Match(normalizedModel);
        if (billionMatch.Success &&
            double.TryParse(billionMatch.Groups["size"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var billions))
        {
            if (billions <= 3.5)
            {
                return PageExtractionProfile.Tiny;
            }

            if (billions <= 8.5)
            {
                return PageExtractionProfile.Small;
            }
        }

        if (normalizedModel.Contains("7b", StringComparison.Ordinal) ||
            normalizedModel.Contains("8b", StringComparison.Ordinal) ||
            normalizedModel.Contains("9b", StringComparison.Ordinal))
        {
            return PageExtractionProfile.Small;
        }

        return PageExtractionProfile.Standard;
    }

    private static PageExtractionProfile ApplyNumericOverrides(PageExtractionProfile profile)
    {
        var maxInputLength = GetIntEnv("PAGE_EXTRACTION_MAX_INPUT_LENGTH") ?? profile.MaxInputLength;
        var maxPromptLinks = GetIntEnv("PAGE_EXTRACTION_MAX_PROMPT_LINKS") ?? profile.MaxPromptLinks;
        var maxSearchResultLinks = GetIntEnv("PAGE_EXTRACTION_MAX_SEARCH_RESULT_LINKS") ?? profile.MaxSearchResultLinks;
        var includeSnapshot = GetBoolEnv("PAGE_EXTRACTION_INCLUDE_SNAPSHOT") ?? profile.IncludeSnapshotInPrompt;
        var snapshotTextLength = GetIntEnv("PAGE_EXTRACTION_SNAPSHOT_TEXT_LENGTH") ?? profile.SnapshotTextLength;
        var snapshotLinks = GetIntEnv("PAGE_EXTRACTION_SNAPSHOT_LINKS") ?? profile.SnapshotLinks;
        var snapshotActions = GetIntEnv("PAGE_EXTRACTION_SNAPSHOT_ACTIONS") ?? profile.SnapshotActions;
        var snapshotTables = GetIntEnv("PAGE_EXTRACTION_SNAPSHOT_TABLES") ?? profile.SnapshotTables;
        var snapshotRows = GetIntEnv("PAGE_EXTRACTION_SNAPSHOT_ROWS") ?? profile.SnapshotRows;

        return profile with
        {
            MaxInputLength = maxInputLength,
            MaxPromptLinks = maxPromptLinks,
            MaxSearchResultLinks = maxSearchResultLinks,
            IncludeSnapshotInPrompt = includeSnapshot,
            SnapshotTextLength = snapshotTextLength,
            SnapshotLinks = snapshotLinks,
            SnapshotActions = snapshotActions,
            SnapshotTables = snapshotTables,
            SnapshotRows = snapshotRows,
        };
    }

    private static int? GetIntEnv(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;
    }

    private static bool? GetBoolEnv(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return bool.TryParse(value, out var parsed)
            ? parsed
            : null;
    }
}
