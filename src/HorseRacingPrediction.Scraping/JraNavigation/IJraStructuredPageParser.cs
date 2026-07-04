using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Scraping.JraNavigation;

public interface IJraStructuredPageParser<T>
    where T : class
{
    JraStructuredPageParseResult<T> Parse(PageSnapshot snapshot);
}