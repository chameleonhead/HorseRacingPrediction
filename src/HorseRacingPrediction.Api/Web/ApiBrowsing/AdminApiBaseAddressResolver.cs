using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace HorseRacingPrediction.Api.Web.ApiBrowsing;

/// <summary>
/// Resolves the base URL the admin UI uses to call this same process's own JSON API,
/// using Kestrel's actual bound address instead of the externally-facing host, so calls
/// never round-trip through a reverse proxy in front of the app.
/// </summary>
public sealed class AdminApiBaseAddressResolver
{
    private readonly IServer _server;
    private Uri? _cached;

    public AdminApiBaseAddressResolver(IServer server)
    {
        _server = server;
    }

    public Uri Resolve()
    {
        if (_cached is not null)
            return _cached;

        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.FirstOrDefault(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            ?? addresses?.FirstOrDefault();

        if (string.IsNullOrEmpty(address))
        {
            throw new InvalidOperationException(
                "自プロセスのリッスンアドレスを解決できませんでした。管理UIから自身のAPIを呼び出すために必要です。");
        }

        var uri = new Uri(address.Replace("://[::]", "://localhost", StringComparison.Ordinal)
            .Replace("://+", "://localhost", StringComparison.Ordinal)
            .Replace("://0.0.0.0", "://localhost", StringComparison.Ordinal));

        return _cached = uri;
    }
}
