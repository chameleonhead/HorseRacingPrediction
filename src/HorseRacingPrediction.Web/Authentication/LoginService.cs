using HorseRacingPrediction.Web.Configurations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace HorseRacingPrediction.Web.Authentication;

public interface ILoginService
{
    bool IsAuthenticated();
    Task<LoginResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);
    Task UnauthenticateAsync(CancellationToken cancellationToken = default);
}

public class LoginService : ILoginService
{
    private IOptions<ApiKeyOptions> _apiKeyOptions;
    private IHttpContextAccessor _contextAccessor;

    public LoginService(IOptions<ApiKeyOptions> apiKeyOptions, IHttpContextAccessor contextAccessor)
    {
        _apiKeyOptions = apiKeyOptions;
        _contextAccessor = contextAccessor;
    }

    public bool IsAuthenticated()
    {
        return _contextAccessor.HttpContext!.User?.Identity?.IsAuthenticated == true;
    }

    public async Task<LoginResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        var apiKey = _apiKeyOptions.Value.Key;
        if (apiKey is null)
        {
            throw new InvalidOperationException("ApiKey configuration is missing.");
        }

        if (password != apiKey)
        {
            return new LoginResult(false);
        }

        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                username),

            new Claim(
                "apikey",
                password)
        };

        var identity = new ClaimsIdentity(
            claims,
            AuthenticationSchemes.Cookie);

        var principal = new ClaimsPrincipal(identity);

        await _contextAccessor.HttpContext!.SignInAsync(
            AuthenticationSchemes.Cookie,
            principal);

        return new LoginResult(true);
    }

    public Task UnauthenticateAsync(CancellationToken cancellationToken = default)
    {
        return _contextAccessor.HttpContext!.SignOutAsync(AuthenticationSchemes.Cookie);
    }
}
