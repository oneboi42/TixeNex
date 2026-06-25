using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDeskHero.Api.IntegrationTests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var testSettings = new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "HelpDeskHero.Tests",
                ["Jwt:Audience"] = "HelpDeskHero.Tests",
                ["Jwt:Key"] = "HelpDeskHero.Tests.Super.Secret.Key.For.Jwt.Token.Signing.123456789",
                ["Jwt:AccessTokenMinutes"] = "60",
                ["Jwt:RefreshTokenDays"] = "7",
                ["ConnectionStrings:DefaultConnection"] = "TestConnection"
            };

            config.AddInMemoryCollection(testSettings);
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));

            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase($"HelpDeskHeroTestsDb-{Guid.NewGuid()}");
            });
        });
    }
}