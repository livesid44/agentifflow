using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using MudBlazor.Services;
using AgentifFlow.BlazorWeb;
using AgentifFlow.BlazorWeb.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ── MudBlazor ────────────────────────────────────────────────────────────────
builder.Services.AddMudServices();

// ── Azure AD authentication (regular tenant, not B2C) ────────────────────────
var apiScope = builder.Configuration["ApiConfig:Scope"]
               ?? "api://YOUR_API_CLIENT_ID/access_as_user";

builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
    options.ProviderOptions.DefaultAccessTokenScopes.Add(apiScope);
    // Use popup for login so the user stays on the same page
    options.ProviderOptions.LoginMode = "popup";
});

// ── Typed HTTP client for the AgentifFlow API (attaches Bearer token) ─────────
var apiBaseUrl = builder.Configuration["ApiConfig:BaseUrl"] ?? "https://localhost:7001";

builder.Services.AddHttpClient<IConfigurationApiService, ConfigurationApiService>(
    client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler(sp =>
        sp.GetRequiredService<AuthorizationMessageHandler>()
          .ConfigureHandler(
              authorizedUrls: [apiBaseUrl],
              scopes: [apiScope]));

await builder.Build().RunAsync();
