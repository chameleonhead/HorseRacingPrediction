using System.Net;

namespace HorseRacingPrediction.Scraping.Jra.Workflow;

/// <summary>
/// APIへの書き込み呼び出しが失敗した際、ワークフロー内で「この1件だけ記録して処理を継続してよい
/// 失敗」なのか「即座にジョブ全体を停止すべき致命的な失敗」なのかを判定する。
/// <para>
/// 502（Bad Gateway）はデプロイ中のAPIコンテナ再起動等による一時的な事象であり、
/// <see cref="HorseRacingPrediction.Collector.Http.TransientBadGatewayRetryHandler"/> が
/// 既に自動リトライを行っている。それでもなお失敗する場合や、502以外の5xx
/// （500・503・504等）が返ってきた場合は、APIサーバー側の実際の異常（未処理例外等）を
/// 示している可能性が高く、他のレース・他の馬の処理を続けても同じ理由で失敗し続けるだけで
/// 無意味なため、ここで検知したらジョブ全体を即座に停止し、原因調査を促す。
/// </para>
/// </summary>
public static class ApiFailureClassifier
{
    public static bool IsFatalServerError(Exception ex)
        => ex is HttpRequestException { StatusCode: { } statusCode }
            && (int)statusCode >= 500
            && statusCode != HttpStatusCode.BadGateway;
}
