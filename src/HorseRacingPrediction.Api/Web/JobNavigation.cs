namespace HorseRacingPrediction.Api.Web;

public static class JobNavigation
{
    public static string DetailUrl(string jobId) =>
        $"/jobs/detail?jobId={Uri.EscapeDataString(jobId)}";

    public static string AbsoluteDetailUrl(string adminBaseUrl, string jobId) =>
        $"{adminBaseUrl.TrimEnd('/')}{DetailUrl(jobId)}";
}
