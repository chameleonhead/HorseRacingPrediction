namespace HorseRacingPrediction.Scraping.Scrapers.Jra;

/// <summary>
/// JRA 出馬表に掲載される賞金見出しごとの着順別金額。
/// </summary>
public sealed record JraRacePrizeData(
    string Type,
    int FinishPosition,
    decimal AmountInTenThousandYen);