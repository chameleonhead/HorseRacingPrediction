using Microsoft.AspNetCore.Authentication.Cookies;

namespace HorseRacingPrediction.Api.Security;

public static class AdminAuthenticationExtensions
{
    public const string AdminUserName = "user";

    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.LogoutPath = "/logout";
                options.AccessDeniedPath = "/login";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Cookie.Name = "HorseRacingPrediction.Admin";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
            });
        services.AddAuthorization();
        services.AddCascadingAuthenticationState();

        return services;
    }
}
