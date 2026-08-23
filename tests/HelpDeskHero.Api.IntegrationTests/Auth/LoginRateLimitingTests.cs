using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HelpDeskHero.Api.IntegrationTests.Infrastructure;
using HelpDeskHero.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HelpDeskHero.Api.IntegrationTests.Auth;

[Collection("ApiIntegration")]
public sealed class LoginRateLimitingTests
{
    private const int LoginPermitLimit = 5;
    private readonly CustomWebApplicationFactory _factory;

    public LoginRateLimitingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WithValidCredentials_StillSucceeds()
    {
        using var factory = CreateRateLimitedFactory();
        using var client = CreateClient(factory, "203.0.113.10");

        var response = await LoginAsync(
            client,
            "user",
            CustomWebApplicationFactory.UserPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_WithFailedAttemptsBelowLimit_RemainsUnauthorized()
    {
        using var factory = CreateRateLimitedFactory();
        using var client = CreateClient(factory, "203.0.113.11");

        for (var attempt = 0; attempt < LoginPermitLimit - 1; attempt++)
        {
            var response = await LoginAsync(
                client,
                "user",
                "IncorrectPassword123!");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Login_WhenClientExceedsLimit_ReturnsTooManyRequests()
    {
        using var factory = CreateRateLimitedFactory();
        using var limitedClient = CreateClient(factory, "203.0.113.12");

        for (var attempt = 0; attempt < LoginPermitLimit; attempt++)
        {
            var response = await LoginAsync(
                limitedClient,
                "user",
                "IncorrectPassword123!");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var rejectedResponse = await LoginAsync(
            limitedClient,
            "user",
            "IncorrectPassword123!");

        rejectedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        using var otherClient = CreateClient(factory, "203.0.113.13");
        var otherClientResponse = await LoginAsync(
            otherClient,
            "user",
            CustomWebApplicationFactory.UserPassword);

        otherClientResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private WebApplicationFactory<Program> CreateRateLimitedFactory() =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["RateLimiting:Login:PermitLimit"] =
                            LoginPermitLimit.ToString()
                    });
            });
        });

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        string forwardedFor)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Forwarded-For",
            forwardedFor);
        return client;
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string userName,
        string password) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            UserName = userName,
            Password = password,
            DeviceName = nameof(LoginRateLimitingTests)
        });
}
