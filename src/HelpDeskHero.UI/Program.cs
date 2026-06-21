using HelpDeskHero.UI;
using HelpDeskHero.UI.Services.Api;
using Blazored.LocalStorage;
using HelpDeskHero.UI.Handlers;
using HelpDeskHero.UI.Services.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddBlazoredLocalStorage();

builder.Services.AddScoped<SessionTokenStore>();

builder.Services.AddScoped<JwtAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<JwtAuthenticationStateProvider>());

builder.Services.AddAuthorizationCore();

builder.Services.AddScoped<AuthSessionService>();
builder.Services.AddScoped<AuthHttpMessageHandler>();

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("Missing Api:BaseUrl.");

// klient bez tokena — tylko login/refresh
builder.Services.AddScoped(sp =>
    new AuthApiClient(new HttpClient
    {
        BaseAddress = new Uri(apiBaseUrl)
    }));

// klient z tokenem — tickets itd.
builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<AuthHttpMessageHandler>();
    handler.InnerHandler = new HttpClientHandler();

    return new HttpClient(handler)
    {
        BaseAddress = new Uri(apiBaseUrl)
    };
});

builder.Services.AddScoped<TicketApiClient>();

await builder.Build().RunAsync();