using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HelpDeskHero.Api.IntegrationTests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"HelpDeskHeroTestsDb-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var testSettings = new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "HelpDeskHero.Tests",
                ["Jwt:Audience"] = "HelpDeskHero.Tests",
                ["Jwt:Key"] = "HelpDeskHero.Tests.Super.Secret.Key.For.Jwt.Token.Signing.123456789",
                ["Jwt:AccessTokenMinutes"] = "60",
                ["Jwt:RefreshTokenDays"] = "7",
                ["SeedUsers:Admin:Password"] = "Admin1234",
                ["SeedUsers:Agent:Password"] = "Agent123!",
                ["SeedUsers:User:Password"] = "User123!",
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
            // AddDbContext registers both the context and its options. Remove all
            //production registrations before adding the InMemory-only test context.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });
        });
    }
}