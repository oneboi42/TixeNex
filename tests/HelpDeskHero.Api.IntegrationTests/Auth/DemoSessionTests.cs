using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using FluentAssertions;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Security;
using HelpDeskHero.Api.IntegrationTests.Infrastructure;
using HelpDeskHero.Shared.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HelpDeskHero.Api.IntegrationTests.Auth;

[Collection("ApiIntegration")]
public sealed class DemoSessionTests
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public DemoSessionTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _factory = factory;
    }

    [Fact]
    public async Task CreateSession_MarksTemporaryUserAndIssuesWorkspaceClaim()
    {
        var response = await _client.PostAsJsonAsync("/api/demo/sessions", new CreateDemoSessionRequestDto
        {
            Role = "User",
            DeviceName = nameof(DemoSessionTests)
        });

        response.EnsureSuccessStatusCode();
        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        tokenResponse.Should().NotBeNull();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokenResponse!.AccessToken);
        jwt.Claims.Single(x => x.Type == "is_demo_workspace").Value.Should().Be("true");
        jwt.Claims.Single(x => x.Type == "is_demo").Value.Should().Be("true");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users
            .AsNoTracking()
            .SingleAsync(x => x.UserName == tokenResponse.UserName);

        user.IsDemoWorkspace.Should().BeTrue();
        user.IsDemoUser.Should().BeTrue();
    }

    [Fact]
    public async Task CreateSession_BelowCapacity_Succeeds()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await CreateSessionAsync(client, "below-capacity");

        response.IsSuccessStatusCode.Should().BeTrue();
        (await CountActiveTemporaryUsersAsync(factory)).Should().Be(1);
    }

    [Fact]
    public async Task CreateSession_AtCapacity_IsRejected()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var maxActiveUsers = GetMaxActiveUsers(factory);
        await SeedActiveTemporaryUsersAsync(factory, maxActiveUsers);

        var response = await CreateSessionAsync(client, "at-capacity");

        response.StatusCode.Should().Be(
            System.Net.HttpStatusCode.ServiceUnavailable);
        (await CountActiveTemporaryUsersAsync(factory))
            .Should().Be(maxActiveUsers);
    }

    [Fact]
    public async Task CreateSession_ConcurrentRequests_NeverExceedCapacity()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var maxActiveUsers = GetMaxActiveUsers(factory);
        await SeedActiveTemporaryUsersAsync(factory, maxActiveUsers - 2);

        const int requestCount = 8;
        var start = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;

        async Task<HttpResponseMessage> SendAsync(int requestNumber)
        {
            if (Interlocked.Increment(ref readyCount) == requestCount)
                allReady.SetResult();

            await start.Task;
            return await CreateSessionAsync(
                client,
                $"concurrent-{requestNumber}");
        }

        var requests = Enumerable.Range(1, requestCount)
            .Select(SendAsync)
            .ToArray();

        await allReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        start.SetResult();

        var responses = await Task.WhenAll(requests);

        responses.Count(response => response.IsSuccessStatusCode)
            .Should().Be(2);
        responses.Count(response =>
                response.StatusCode ==
                System.Net.HttpStatusCode.ServiceUnavailable)
            .Should().Be(requestCount - 2);

        var finalActiveUsers =
            await CountActiveTemporaryUsersAsync(factory);
        finalActiveUsers.Should().Be(maxActiveUsers);
        finalActiveUsers.Should().BeLessThanOrEqualTo(maxActiveUsers);
    }

    private static Task<HttpResponseMessage> CreateSessionAsync(
        HttpClient client,
        string deviceName) =>
        client.PostAsJsonAsync(
            "/api/demo/sessions",
            new CreateDemoSessionRequestDto
            {
                Role = "User",
                DeviceName = deviceName
            });

    private static int GetMaxActiveUsers(
        CustomWebApplicationFactory factory) =>
        factory.Services
            .GetRequiredService<IOptions<DemoOptions>>()
            .Value
            .MaxActiveUsers;

    private static async Task SeedActiveTemporaryUsersAsync(
        CustomWebApplicationFactory factory,
        int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        db.Users.AddRange(
            Enumerable.Range(1, count).Select(index =>
                new HelpDeskHero.Api.Domain.ApplicationUser
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserName = $"capacity-user-{index}",
                    NormalizedUserName = $"CAPACITY-USER-{index}",
                    DisplayName = $"Capacity User {index}",
                    IsActive = true,
                    IsDemoWorkspace = true,
                    IsDemoUser = true,
                    CreatedAtUtc = now,
                    DemoExpiresAtUtc = now.AddMinutes(30),
                    DemoAbsoluteExpiresAtUtc = now.AddMinutes(90)
                }));

        await db.SaveChangesAsync();
    }

    private static async Task<int> CountActiveTemporaryUsersAsync(
        CustomWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        return await db.Users.CountAsync(user =>
            user.IsDemoUser &&
            user.IsActive &&
            user.DemoExpiresAtUtc != null &&
            user.DemoExpiresAtUtc > now);
    }
}
