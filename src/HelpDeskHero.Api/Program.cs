using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.SqlServer;
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Application.Services;
using HelpDeskHero.Api.Application.TicketVisibility;
using HelpDeskHero.Api.BackgroundJobs;
using HelpDeskHero.Api.BackgroundJobs.Contracts;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Background;
using HelpDeskHero.Api.Infrastructure.Notifications;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Security;
using HelpDeskHero.Api.Infrastructure.Services;
using HelpDeskHero.Api.Infrastructure.Storage;
using HelpDeskHero.Api.Hubs;
using HelpDeskHero.Api.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

using Microsoft.Extensions.Options;
using Minio;

var builder = WebApplication.CreateBuilder(args);
var isTesting = builder.Environment.IsEnvironment("Testing");

const string CorsPolicyName = "BlazorUi";
const string LoginRateLimitPolicyName = "login";

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy.WithOrigins(
                "https://localhost:7045",
                "http://localhost:5045",
                "http://localhost:8080")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, NameIdentifierUserIdProvider>();
builder.Services.AddEndpointsApiExplorer();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;

    options.KnownIPNetworks.Add(
        new System.Net.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
    options.KnownIPNetworks.Add(
        new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
    options.KnownIPNetworks.Add(
        new System.Net.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(LoginRateLimitPolicyName, httpContext =>
    {
        var permitLimit = httpContext.RequestServices
            .GetRequiredService<IConfiguration>()
            .GetValue("RateLimiting:Login:PermitLimit", 5);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey:
                httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            });
    });
});

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter a JWT token. Example: Bearer eyJhbGciOiJIUzI1NiIs..."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// JWT configuration
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

builder.Services
    .AddOptions<DemoOptions>()
    .Bind(builder.Configuration.GetSection(DemoOptions.SectionName))
    .Validate(
        options => options.SlidingLifetimeMinutes > 0,
        "Demo:SlidingLifetimeMinutes must be greater than zero.")
    .Validate(
        options => options.AbsoluteLifetimeMinutes >=
                   options.SlidingLifetimeMinutes,
        "Demo:AbsoluteLifetimeMinutes must be greater than or equal to the sliding lifetime.")
    .Validate(
        options => options.MaxActiveUsers > 0,
        "Demo:MaxActiveUsers must be greater than zero.")
    .Validate(
        options => options.AllowedRoles.Length > 0,
        "Demo:AllowedRoles must contain at least one role.")
    .ValidateOnStart();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IDemoCleanupJob, DemoCleanupJob>();

if (!isTesting)
{
    builder.Services.AddHangfire(config =>
    {
        config.UseSimpleAssemblyNameTypeSerializer()
              .UseRecommendedSerializerSettings()
              .UseSqlServerStorage(
                  builder.Configuration.GetConnectionString("DefaultConnection"),
                  new SqlServerStorageOptions
                  {
                      PrepareSchemaIfNecessary = true
                  });
    });

    builder.Services.AddHangfireServer();
    builder.Services.AddScoped<INotificationJob, NotificationJob>();
}

// Identity configuration
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = false;

    options.User.RequireUniqueEmail = false;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AppDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

// TokenService registration
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<ISlaCalculator, SlaCalculator>();
builder.Services.AddScoped<ITicketAssignmentService, TicketAssignmentService>();
builder.Services.AddScoped<ITicketVisibilityContextResolver, TicketVisibilityContextResolver>();
builder.Services.AddScoped<ISlaMonitorService, SlaMonitorService>();
builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();
builder.Services.AddScoped<IMessagePublisher, RabbitMqPublisher>();
builder.Services.AddScoped<ITicketLiveNotifier, SignalRTicketLiveNotifier>();
builder.Services.AddHostedService<OutboxProcessorService>();
builder.Services.AddHostedService<SlaWatchdogService>();

// JWT authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, configuration) =>
    {
        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>()
                  ?? throw new InvalidOperationException("Missing Jwt settings.");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrWhiteSpace(accessToken) &&
                    path.StartsWithSegments("/hubs/tickets"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

// Register AuditService and HttpContextAccessor
builder.Services.AddScoped<AuditService>();
builder.Services.AddHttpContextAccessor();

// Notification dispatcher registration
builder.Services.AddScoped<INotificationSender, InAppNotificationSender>();
builder.Services.AddScoped<INotificationSender, EmailNotificationSender>();
builder.Services.AddHttpClient<WebhookNotificationSender>();
builder.Services.AddScoped<INotificationSender, WebhookNotificationSender>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("AgentOrAdmin", policy => policy.RequireRole("Agent", "Admin"));
    options.AddPolicy("CanManageTickets", policy => policy.RequireRole("User", "Agent", "Admin"));
    options.AddPolicy("CanViewAudit", policy => policy.RequireRole("Admin"));
});

// Minio configuration
builder.Services
    .AddOptions<MinioOptions>()
    .Bind(
        builder.Configuration.GetSection(
            MinioOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Endpoint),
        "Minio:Endpoint is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.AccessKey),
        "Minio:AccessKey is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.SecretKey),
        "Minio:SecretKey is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.BucketName),
        "Minio:BucketName is required.")
    .ValidateOnStart();

builder.Services.AddSingleton<IMinioClient>(
    serviceProvider =>
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<MinioOptions>>()
            .Value;

        return new MinioClient()
            .WithEndpoint(options.Endpoint)
            .WithCredentials(
                options.AccessKey,
                options.SecretKey)
            .WithSSL(options.UseSsl)
            .Build();
    });

builder.Services.AddScoped<
    IExportObjectStorage,
    MinioExportObjectStorage>();


var app = builder.Build();

// Apply EF Core migrations and seed database on startup.
await DbSeeder.SeedAsync(app.Services);

app.UseForwardedHeaders();

// Global exception handling middleware
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (builder.Configuration.GetValue("UseHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}

app.UseCors(CorsPolicyName);
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire");
}

if (!app.Environment.IsEnvironment("Testing"))
{
    var recurringJobManager =
        app.Services
            .GetRequiredService<IRecurringJobManager>();

    recurringJobManager.AddOrUpdate<INotificationJob>(
        "daily-summary",
        job => job.SendDailySummaryAsync(default),
        "0 7 * * *");

    var demoOptions = app.Services
        .GetRequiredService<IOptions<DemoOptions>>()
        .Value;

    if (demoOptions.Enabled)
    {
        recurringJobManager.AddOrUpdate<IDemoCleanupJob>(
            "demo-cleanup",
            job =>
                job.CleanupExpiredDemoDataAsync(
                    default),
            "*/10 * * * *");
    }
    else
    {
        recurringJobManager.RemoveIfExists(
            "demo-cleanup");
    }
}

app.MapControllers();
app.MapHub<TicketsHub>("/hubs/tickets");

// Redirect root URL to Swagger UI
app.MapGet("/", async context =>
{
    context.Response.Redirect("/swagger/index.html", permanent: false);
    await Task.CompletedTask;
});

app.Run();

public partial class Program;
