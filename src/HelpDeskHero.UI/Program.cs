using HelpDeskHero.UI;
using HelpDeskHero.UI.Services.Api;
using HelpDeskHero.UI.Services.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("Missing Api:BaseUrl.");

builder.Services.AddScoped<TokenStorageService>();
builder.Services.AddScoped<AuthTokenHandler>();
builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthenticationStateProvider>();
builder.Services.AddAuthorizationCore();

builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<AuthTokenHandler>();
    handler.InnerHandler = new HttpClientHandler();

    return new HttpClient(handler)
    {
        BaseAddress = new Uri(apiBaseUrl)
    };
});

builder.Services.AddScoped<TicketApiClient>();
builder.Services.AddScoped<AuthService>();

await builder.Build().RunAsync();