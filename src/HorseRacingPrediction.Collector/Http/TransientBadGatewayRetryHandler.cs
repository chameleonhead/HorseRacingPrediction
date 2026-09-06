using System.Linq;
using System.Net;

namespace HorseRacingPrediction.Collector.Http;

/// <summary>
/// APIサーバーがデプロイ中の再起動等で一時的に502（Bad Gateway）を返す場合に、
/// 数秒待って自動的にリトライする<see cref="DelegatingHandler"/>。
/// 502はリバースプロキシ（Caddy）がアップストリームに到達できない場合に即座に
/// 返されるものであり、通常は数秒でAPIコンテナの起動が完了して解消する一時的な
/// 事象であるため、ジョブ全体を失敗させる前にここで吸収する。
/// </summary>
public sealed class TransientBadGatewayRetryHandler : DelegatingHandler
{
    private const int MaxRetryCount = 2;
    private readonly TimeSpan _retryDelay;

    public TransientBadGatewayRetryHandler()
        : this(TimeSpan.FromSeconds(3))
    {
    }

    /// <summary>テスト用に待機時間を差し替え可能にするコンストラクタ。</summary>
    internal TransientBadGatewayRetryHandler(TimeSpan retryDelay)
    {
        _retryDelay = retryDelay;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // HttpRequestMessage・そのContentは一度送信すると再利用できないため、
        // 本文を事前にバイト列として読み切っておき、送信のたびに新しい
        // HttpRequestMessage/ByteArrayContentを組み立て直す。
        byte[]? body = null;
        string[]? contentHeaderNames = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            contentHeaderNames = request.Content.Headers.Select(x => x.Key).ToArray();
        }

        for (var attempt = 0; ; attempt++)
        {
            using var attemptRequest = BuildAttemptRequest(request, body, contentHeaderNames);
            var response = await base.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.BadGateway || attempt >= MaxRetryCount)
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static HttpRequestMessage BuildAttemptRequest(
        HttpRequestMessage original,
        byte[]? body,
        string[]? contentHeaderNames)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
        };

        if (body is not null)
        {
            var content = new ByteArrayContent(body);
            foreach (var name in contentHeaderNames!)
            {
                if (original.Content!.Headers.TryGetValues(name, out var values))
                {
                    content.Headers.TryAddWithoutValidation(name, values);
                }
            }

            clone.Content = content;
        }

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in original.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        return clone;
    }
}
