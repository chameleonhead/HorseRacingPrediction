using HorseRacingPrediction.Scraping.Jra.Models;
using HorseRacingPrediction.Scraping.Jra.Parsing;

namespace HorseRacingPrediction.Scraping.Jra.Navigation;

public sealed partial class JraNavigator
{
    public async Task<JraMeetingCancellation?> ReadMeetingCancellationAsync(DateOnly date, RaceCourse course,
        CancellationToken cancellationToken = default)
    {
        // This is a public, dated static programme URL, not a reconstructed JRADB session URL.
        // The parser verifies the final official URL, displayed date, and table identity again.
        await _browser.NavigateForSnapshotAsync(JraMeetingCancellationParser.ProgrammeUrl(date).AbsoluteUri,
            cancellationToken).ConfigureAwait(false);
        var snapshot = await _browser.GetDataPageSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return JraMeetingCancellationParser.Parse(snapshot, date, course);
    }
}
