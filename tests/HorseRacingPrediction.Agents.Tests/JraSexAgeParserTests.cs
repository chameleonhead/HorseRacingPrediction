// JRAサイト再設計（docs/jra-scraping.md）により、対象の JraSexAgeParser は一時的に無効化されている。
#if false
using HorseRacingPrediction.Scraping.JraNavigation;

namespace HorseRacingPrediction.Agents.Tests;

[TestClass]
public sealed class JraSexAgeParserTests
{
    [TestMethod]
    public void Parse_ConvertsSexAgeVariants_ToCanonicalCode()
    {
        var cases = new (string SexAge, string ExpectedCode, int ExpectedAge)[]
        {
            ("牡3", "M", 3),
            ("牝4", "F", 4),
            ("セ5", "G", 5),
            ("せん6", "G", 6),
            ("騙7", "G", 7),
            ("G8", "G", 8),
            ("C9", "G", 9),
        };

        foreach (var (sexAge, expectedCode, expectedAge) in cases)
        {
            var (sexCode, age) = JraSexAgeParser.Parse(sexAge);
            Assert.AreEqual(expectedCode, sexCode);
            Assert.AreEqual(expectedAge, age);
        }
    }
}
#endif
