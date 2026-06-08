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

builder.Services.AddScoped<TokenStorageService>();
builder.Services.AddScoped<SessionTokenStore>();
builder.Services.AddScoped<AuthSessionService>();

builder.Services.AddScoped<JwtAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<JwtAuthenticationStateProvider>());

builder.Services.AddAuthorizationCore();

builder.Services.AddScoped<AuthTokenHandler>();
builder.Services.AddScoped<AuthHttpMessageHandler>();

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("Missing Api:BaseUrl.");

builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<AuthHttpMessageHandler>();
    handler.InnerHandler = new HttpClientHandler();

    return new HttpClient(handler)
    {
        BaseAddress = new Uri(apiBaseUrl)
    };
});

builder.Services.AddScoped<AuthApiClient>();
builder.Services.AddScoped<TicketApiClient>();
builder.Services.AddScoped<AuthService>();

await builder.Build().RunAsync();