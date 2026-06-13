namespace HorseRacingPrediction.Scraping.Browser;

public sealed record PageImageSnapshot(
    string Url,
    string Alt,
    string Title,
    string Region);