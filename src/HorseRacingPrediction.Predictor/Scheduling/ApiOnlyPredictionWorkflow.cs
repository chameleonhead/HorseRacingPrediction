using System.Globalization;
using HorseRacingPrediction.Contracts;
using HorseRacingPrediction.ApiClient;
using Microsoft.Extensions.Logging;

namespace HorseRacingPrediction.Predictor.Scheduling;

public sealed class ApiOnlyPredictionWorkflow
{
    private readonly IRaceQueryService _raceQueryService;
    private readonly IPredictionWriteService _predictionWriteService;
    private readonly ILogger<ApiOnlyPredictionWorkflow> _logger;

    public ApiOnlyPredictionWorkflow(
        IRaceQueryService raceQueryService,
        IPredictionWriteService predictionWriteService,
        ILogger<ApiOnlyPredictionWorkflow> logger)
    {
        _raceQueryService = raceQueryService;
        _predictionWriteService = predictionWriteService;
        _logger = logger;
    }

    public async Task<ApiOnlyPredictionResult> RunAsync(string raceId, CancellationToken cancellationToken = default)
    {
        var context = await _raceQueryService.GetRacePredictionContextAsync(raceId, cancellationToken).ConfigureAwait(false);
        if (context is null || string.IsNullOrWhiteSpace(context.RaceId))
        {
            throw new InvalidOperationException($"Race context was not found via API. RaceId={raceId}");
        }

        var activeEntries = context.Entries.Where(x => x.ParticipationStatus == RaceEntryParticipationStatus.Active).ToArray();
        if (context.Entries.Count > 0 && activeEntries.Length == 0)
            return new ApiOnlyPredictionResult(string.Empty, "取消・除外のため予想対象馬がありません。出馬表は保持しています。", Skipped: true);

        if (activeEntries.Length == 0 || activeEntries.Any(x => x.HorseNumber is null or <= 0 || x.GateNumber is null))
        {
            throw new InvalidOperationException($"Race context has no entries. RaceId={raceId}");
        }

        var mlPrediction = await _raceQueryService.GetMlPredictionAsync(raceId, cancellationToken).ConfigureAwait(false);
        var rankings = BuildRankings(context, mlPrediction);
        if (rankings.Count == 0)
        {
            throw new InvalidOperationException($"No ranking candidates available. RaceId={raceId}");
        }

        var summary = BuildSummary(context, rankings, mlPrediction is not null);
        var confidence = Math.Clamp(rankings.Average(x => x.Score) / 100m, 0m, 1m);

        var predictionTicketId = await _predictionWriteService.CreateBoundPredictionTicketAsync(
            raceId,
            predictorType: "ApiOnlyPredictor",
            predictorId: "api-only-v1",
            confidenceScore: confidence,
            summaryComment: summary,
            entryAssignmentFingerprint: context.EntryAssignmentFingerprint,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var ranking in rankings)
        {
            await _predictionWriteService.AddPredictionMarkAsync(
                predictionTicketId,
                ranking.EntryId,
                MapMarkCode(ranking.PredictedRank),
                ranking.PredictedRank,
                ranking.Score,
                ranking.Comment,
                cancellationToken).ConfigureAwait(false);

            await _predictionWriteService.AddPredictionRationaleAsync(
                predictionTicketId,
                subjectType: "RaceEntry",
                subjectId: ranking.EntryId,
                signalType: "ApiMlScore",
                signalValue: ranking.Score.ToString("0.###", CultureInfo.InvariantCulture),
                explanationText: ranking.Comment,
                cancellationToken).ConfigureAwait(false);
        }

        await _predictionWriteService.FinalizePredictionTicketAsync(predictionTicketId, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "[予想-API_ONLY] 予想票を作成しました。RaceId={RaceId} TicketId={TicketId} RankingCount={RankingCount}",
            raceId,
            predictionTicketId,
            rankings.Count);

        return new ApiOnlyPredictionResult(predictionTicketId, summary);
    }

    private static List<ApiOnlyPredictionRanking> BuildRankings(
        RacePredictionContextReadModel context,
        MlPredictionResponse? mlPrediction)
    {
        var entriesById = context.Entries
            .Where(x => x.ParticipationStatus == RaceEntryParticipationStatus.Active && !string.IsNullOrWhiteSpace(x.EntryId))
            .ToDictionary(x => x.EntryId, x => x, StringComparer.Ordinal);

        if (mlPrediction is not null && mlPrediction.Rankings.Count > 0)
        {
            return mlPrediction.Rankings
                .Where(x => entriesById.ContainsKey(x.EntryId))
                .OrderBy(x => x.PredictedRank)
                .ThenByDescending(x => x.PredictedScore)
                .Select(x => new ApiOnlyPredictionRanking(
                    x.EntryId,
                    x.PredictedRank,
                    Math.Clamp((decimal)x.PredictedScore, 0m, 100m),
                    $"APIのML予測スコア {x.PredictedScore:0.###} に基づく順位 {x.PredictedRank}"))
                .ToList();
        }

        return context.Entries
            .Where(x => x.ParticipationStatus == RaceEntryParticipationStatus.Active)
            .OrderBy(x => x.HorseNumber)
            .Select((x, index) => new ApiOnlyPredictionRanking(
                x.EntryId,
                index + 1,
                Math.Max(0m, 100m - (index * 5m)),
                "ML予測が取得できなかったため、APIの出走情報順で暫定順位を作成"))
            .ToList();
    }

    private static string BuildSummary(
        RacePredictionContextReadModel context,
        IReadOnlyList<ApiOnlyPredictionRanking> rankings,
        bool usedMl)
    {
        var top3 = rankings.Take(3).Select(x => $"{x.PredictedRank}位:{x.EntryId}");
        var topSummary = string.Join(", ", top3);
        var source = usedMl
            ? "APIのRaceContext + APIのML予測"
            : "APIのRaceContext（ML未取得のため暫定ロジック）";

        return $"{context.RaceId} の予想を {source} だけで生成。上位: {topSummary}";
    }

    private static string MapMarkCode(int rank)
    {
        return rank switch
        {
            1 => "◎",
            2 => "○",
            3 => "▲",
            4 => "△",
            _ => "☆"
        };
    }
}

public sealed record ApiOnlyPredictionResult(string PredictionTicketId, string PredictionSummary, bool Skipped = false);

internal sealed record ApiOnlyPredictionRanking(
    string EntryId,
    int PredictedRank,
    decimal Score,
    string Comment);
