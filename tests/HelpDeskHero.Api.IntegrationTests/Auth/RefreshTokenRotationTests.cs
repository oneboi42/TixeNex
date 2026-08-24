using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.IntegrationTests.Infrastructure;
using HelpDeskHero.Shared.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDeskHero.Api.IntegrationTests.Auth;

[Collection("ApiIntegration")]
public sealed class RefreshTokenRotationTests
{
    [Fact]
    public async Task Refresh_ConcurrentUseOfOneToken_CreatesOneUsableSuccessor()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client);

        const int requestCount = 4;
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
            return await RefreshAsync(
                client,
                login.RefreshToken,
                $"concurrent-refresh-{requestNumber}");
        }

        var requests = Enumerable.Range(1, requestCount)
            .Select(SendAsync)
            .ToArray();

        await allReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        start.SetResult();

        var responses = await Task.WhenAll(requests);
        var successfulResponse = responses
            .Should()
            .ContainSingle(response => response.IsSuccessStatusCode)
            .Subject;

        responses.Where(response => response != successfulResponse)
            .Should()
            .OnlyContain(response =>
                response.StatusCode == HttpStatusCode.Unauthorized);

        var successor = await successfulResponse.Content
            .ReadFromJsonAsync<TokenResponseDto>();
        successor.Should().NotBeNull();

        (await CountActiveAdminRefreshTokensAsync(factory))
            .Should().Be(1);

        var originalReplay = await RefreshAsync(
            client,
            login.RefreshToken,
            "original-replay");
        originalReplay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var successorUse = await RefreshAsync(
            client,
            successor!.RefreshToken,
            "successor-use");
        successorUse.IsSuccessStatusCode.Should().BeTrue();

        (await CountActiveAdminRefreshTokensAsync(factory))
            .Should().Be(1);
    }

    [Fact]
    public async Task Refresh_SequentialRotation_SucceedsAndRejectsReplay()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var login = await LoginAsync(client);

        var firstRotation = await RefreshAsync(
            client,
            login.RefreshToken,
            "first-rotation");
        firstRotation.IsSuccessStatusCode.Should().BeTrue();
        var firstSuccessor = await firstRotation.Content
            .ReadFromJsonAsync<TokenResponseDto>();
        firstSuccessor.Should().NotBeNull();

        var replay = await RefreshAsync(
            client,
            login.RefreshToken,
            "sequential-replay");
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var secondRotation = await RefreshAsync(
            client,
            firstSuccessor!.RefreshToken,
            "second-rotation");
        secondRotation.IsSuccessStatusCode.Should().BeTrue();
    }

    private static async Task<TokenResponseDto> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                UserName = "admin",
                Password = CustomWebApplicationFactory.AdminPassword,
                DeviceName = "refresh-rotation-test"
            });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponseDto>())!;
    }

    private static Task<HttpResponseMessage> RefreshAsync(
        HttpClient client,
        string refreshToken,
        string deviceName) =>
        client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequestDto
            {
                RefreshToken = refreshToken,
                DeviceName = deviceName
            });

    private static async Task<int> CountActiveAdminRefreshTokensAsync(
        CustomWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        return await db.RefreshTokens
            .CountAsync(token =>
                token.User != null &&
                token.User.UserName == "admin" &&
                token.RevokedAtUtc == null &&
                token.ExpiresAtUtc > now);
    }
}
