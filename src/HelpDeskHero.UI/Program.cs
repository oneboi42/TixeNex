using HelpDeskHero.UI;
using HelpDeskHero.UI.Services.Api;
using HelpDeskHero.UI.Services.Auth;
using HelpDeskHero.UI.Services.Realtime;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("Missing Api:BaseUrl.");

builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy("CanManageTickets", policy =>
        policy.RequireRole("Admin", "Agent"));

    options.AddPolicy("CanViewAudit", policy =>
        policy.RequireRole("Admin"));
});

builder.Services.AddScoped<TokenStore>();

builder.Services.AddScoped<JwtAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<JwtAuthenticationStateProvider>());

builder.Services.AddScoped<AuthHttpMessageHandler>();

builder.Services.AddHttpClient("AnonymousApi", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

builder.Services.AddHttpClient("Api", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
})
.AddHttpMessageHandler<AuthHttpMessageHandler>();

builder.Services.AddHttpClient("AuthorizedApi", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
})
.AddHttpMessageHandler<AuthHttpMessageHandler>();

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api"));

builder.Services.AddScoped<AuthApiClient>();
builder.Services.AddScoped<ITicketApiClient, TicketApiClient>();
builder.Services.AddScoped<UserApiClient>();
builder.Services.AddScoped<TicketCommentApiClient>();
builder.Services.AddScoped<TicketAttachmentApiClient>();
builder.Services.AddScoped<NotificationApiClient>();
builder.Services.AddScoped<DashboardApiClient>();
builder.Services.AddScoped<ExportApiClient>();

builder.Services.AddScoped<TicketsRealtimeClient>();
builder.Services.AddScoped<ITicketsRealtimeClient>(sp => sp.GetRequiredService<TicketsRealtimeClient>());
builder.Services.AddScoped<INotificationRealtimeClient>(sp => sp.GetRequiredService<TicketsRealtimeClient>());
builder.Services.AddScoped<NotificationRealtimeCoordinator>();

await builder.Build().RunAsync();
