using TixeNex.Api.BackgroundJobs;
using TixeNex.Api.BackgroundJobs.Contracts;
using TixeNex.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TixeNex.Api.IntegrationTests.Infrastructure;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "Admin1234";
    public const string AgentPassword = "Agent123!";
    public const string UserPassword = "User123!";

    private readonly string _databaseName =
        $"TixeNexTestsDb-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var testSettings = new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "TixeNex.Tests",
                ["Jwt:Audience"] = "TixeNex.Tests",
                ["Jwt:Key"] =
                    "TixeNex.Tests.Super.Secret.Key.For.Jwt.Token.Signing.123456789",
                ["Jwt:AccessTokenMinutes"] = "60",
                ["Jwt:RefreshTokenDays"] = "7",
                ["Demo:Enabled"] = "true",
                ["RateLimiting:Login:PermitLimit"] = "1000",
                ["RateLimiting:DemoSession:PermitLimit"] = "1000",

                ["SeedUsers:Admin:Password"] = AdminPassword,
                ["SeedUsers:Agent:Password"] = AgentPassword,
                ["SeedUsers:User:Password"] = UserPassword,

                ["SeedUsers:ResetPasswords"] = "false",

                ["Minio:Endpoint"] = "localhost:9000",
                ["Minio:AccessKey"] = "test-access-key",
                ["Minio:SecretKey"] = "test-secret-key",
                ["Minio:BucketName"] = "test-exports",
                ["Minio:UseSsl"] = "false"
            };

            config.AddInMemoryCollection(testSettings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            services.AddScoped<INotificationJob, NotificationJob>();
        });
    }
}
