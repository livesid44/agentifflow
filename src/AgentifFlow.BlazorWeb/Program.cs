using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor.Services;
using AgentifFlow.BlazorWeb;
using AgentifFlow.BlazorWeb.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ── MudBlazor ────────────────────────────────────────────────────────────────
builder.Services.AddMudServices();

var apiBaseUrl      = builder.Configuration["ApiConfig:BaseUrl"]      ?? "https://localhost:7001";
var enableDevLogin  = builder.Configuration.GetValue<bool>("ApiConfig:EnableDevLogin");

if (enableDevLogin)
{
    // ── Dev local auth (no MSAL required) ────────────────────────────────────
    // Authorization infrastructure — CascadingAuthenticationState in App.razor
    // already cascades the state; we only need the core services here.
    builder.Services.AddAuthorizationCore();
    builder.Services.AddSingleton<LocalAuthStateProvider>();
    builder.Services.AddSingleton<AuthenticationStateProvider>(
        sp => sp.GetRequiredService<LocalAuthStateProvider>());
    builder.Services.AddTransient<DevAuthHandler>();

    // Plain (unauthenticated) client used to POST /api/auth/local-login before login.
    builder.Services.AddHttpClient<ILocalAuthApiService, LocalAuthApiService>(
        client => client.BaseAddress = new Uri(apiBaseUrl));

    // Authenticated client — DevAuthHandler attaches the stored JWT automatically.
    builder.Services.AddHttpClient<IConfigurationApiService, ConfigurationApiService>(
        client => client.BaseAddress = new Uri(apiBaseUrl))
        .AddHttpMessageHandler<DevAuthHandler>();

    builder.Services.AddHttpClient<IBlobJobApiService, BlobJobApiService>(
        client => client.BaseAddress = new Uri(apiBaseUrl))
        .AddHttpMessageHandler<DevAuthHandler>();
}
else
{
    // ── Production Azure AD / MSAL auth ──────────────────────────────────────
    var apiScope = builder.Configuration["ApiConfig:Scope"]
                   ?? "api://YOUR_API_CLIENT_ID/access_as_user";

    builder.Services.AddMsalAuthentication(options =>
    {
        builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
        options.ProviderOptions.DefaultAccessTokenScopes.Add(apiScope);
        options.ProviderOptions.LoginMode = "popup";
    });

    builder.Services.AddHttpClient<IConfigurationApiService, ConfigurationApiService>(
        client => client.BaseAddress = new Uri(apiBaseUrl))
        .AddHttpMessageHandler(sp =>
            sp.GetRequiredService<AuthorizationMessageHandler>()
              .ConfigureHandler(
                  authorizedUrls: [apiBaseUrl],
                  scopes: [apiScope]));

    builder.Services.AddHttpClient<IBlobJobApiService, BlobJobApiService>(
        client => client.BaseAddress = new Uri(apiBaseUrl))
        .AddHttpMessageHandler(sp =>
            sp.GetRequiredService<AuthorizationMessageHandler>()
              .ConfigureHandler(
                  authorizedUrls: [apiBaseUrl],
                  scopes: [apiScope]));
}

await builder.Build().RunAsync();
