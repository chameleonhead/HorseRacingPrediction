using HorseRacingPrediction.Scraping.Browser;

namespace HorseRacingPrediction.Agents.JraAgent;

public interface IJraStructuredPageParser<T>
    where T : class
{
    JraStructuredPageParseResult<T> Parse(PageSnapshot snapshot);
}