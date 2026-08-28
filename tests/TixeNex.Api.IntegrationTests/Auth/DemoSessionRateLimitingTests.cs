using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TixeNex.Api.Controllers;
using TixeNex.Api.IntegrationTests.Infrastructure;
using TixeNex.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace TixeNex.Api.IntegrationTests.Auth;

public sealed class DemoSessionRateLimitingTests
{
    [Fact]
    public void CreateSession_UsesDemoSessionRateLimitPolicy()
    {
        var attribute = typeof(DemoController)
            .GetMethod(nameof(DemoController.CreateSession))!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), false)
            .Cast<EnableRateLimitingAttribute>()
            .Single();

        attribute.PolicyName.Should().Be("demo-session");
    }

    [Fact]
    public async Task CreateSession_ExceedingConfiguredLimit_ReturnsTooManyRequests()
    {
        await using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:DemoSession:PermitLimit"] = "3"
                });
            });
        });
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await CreateSessionAsync(client);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var rejectedResponse = await CreateSessionAsync(client);

        rejectedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private static Task<HttpResponseMessage> CreateSessionAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/demo/sessions", new CreateDemoSessionRequestDto
        {
            Role = "User",
            DeviceName = nameof(DemoSessionRateLimitingTests)
        });
}
