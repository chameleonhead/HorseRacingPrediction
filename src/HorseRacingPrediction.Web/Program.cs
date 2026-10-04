using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.ApiClient.Races;
using HorseRacingPrediction.Web.Authentication;
using HorseRacingPrediction.Web.Components;
using HorseRacingPrediction.Web.Configurations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiKeyOptions>(options =>
{
    options.HeaderName = builder.Configuration["ApiKey:HeaderName"] ?? "X-Api-Key";
    var configuredKey = builder.Configuration["ApiKey:Key"];
    options.Key = string.IsNullOrWhiteSpace(configuredKey)
        ? Environment.GetEnvironmentVariable("HORSE_RACING_API_KEY")
        : configuredKey;
});

var dataProtectionKeysDirectory = builder.Configuration["DataProtection:KeysDirectory"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysDirectory))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDirectory));
}

// Add services to the container.
builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();
builder.Services.AddHorseRacingApiClient(options =>
{
    var baseUrl = builder.Configuration["ApiClient:BaseUrl"]
        ?? throw new InvalidOperationException("ApiClient:BaseUrl must be configured.");
    options.BaseAddress = new Uri(baseUrl);
    options.ApiKey = builder.Configuration["ApiClient:ApiKey"] ?? Environment.GetEnvironmentVariable("HORSE_RACING_API_KEY");
    options.ApiKeyHeaderName = builder.Configuration["ApiClient:ApiKeyHeaderName"] ?? "X-Api-Key";
});
builder.Services.AddScoped(sp => sp.GetRequiredService<IApiClientFactory>().Create<IRacesApi>());

// Add services for authentication and authorization
builder.Services
    .AddAuthentication(AuthenticationSchemes.Cookie)
    .AddCookie(AuthenticationSchemes.Cookie, options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";

        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ILoginService, LoginService>();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.Map("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(AuthenticationSchemes.Cookie);
    return TypedResults.LocalRedirect("/");
}).RequireAuthorization();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
