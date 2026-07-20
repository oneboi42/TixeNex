using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Storage;
using HelpDeskHero.Worker.Messaging;
using HelpDeskHero.Worker.Services;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IFileStorage, WorkerFileStorage>();

builder.Services.AddScoped<IExportService, ExportService>();

builder.Services.AddHostedService<RabbitMqConsumer>();

var host = builder.Build();
host.Run();