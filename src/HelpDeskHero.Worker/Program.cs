using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Worker.Messaging;
using HelpDeskHero.Worker.Services;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

builder.Services.AddScoped<IExportFileStorage, WorkerFileStorage>();

builder.Services.AddScoped<IExportService, ExportService>();

builder.Services.AddHostedService<RabbitMqConsumer>();

var host = builder.Build();

await host.RunAsync();