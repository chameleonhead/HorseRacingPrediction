namespace HorseRacingPrediction.Scraping.Jra.Pages;

public interface IJraPage
{
    JraPageKind Kind { get; }

    string Url { get; }
}