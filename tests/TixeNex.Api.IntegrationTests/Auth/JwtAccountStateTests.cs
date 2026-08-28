using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using TixeNex.Api.Domain;
using TixeNex.Api.Infrastructure.Services;
using TixeNex.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace TixeNex.Api.IntegrationTests.Auth;

public sealed class JwtAccountStateTests
{
    [Fact]
    public async Task ActiveUsersExistingToken_RemainsAuthenticatedForItsRole()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(
            factory,
            client,
            "user");

        var response = await client.GetAsync("/api/exports/options");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DisabledUsersExistingToken_IsRejected()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(
            factory,
            client,
            "user");
        await UpdateUserAsync(factory, "user", user => user.IsActive = false);

        var response = await client.GetAsync("/api/exports/options");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeletedUsersExistingToken_IsRejected()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(
            factory,
            client,
            "user");

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByNameAsync("user");
            var result = await userManager.DeleteAsync(user!);
            result.Succeeded.Should().BeTrue();
        }

        var response = await client.GetAsync("/api/exports/options");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DemotedAdminsExistingToken_NoLongerHasAdminAuthorization()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        await AuthenticateAsync(
            factory,
            client,
            "admin");

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await userManager.FindByNameAsync("admin");
            var result = await userManager.RemoveFromRoleAsync(admin!, "Admin");
            result.Succeeded.Should().BeTrue();
        }

        var response = await client.GetAsync("/api/users/agents");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task AuthenticateAsync(
        CustomWebApplicationFactory factory,
        HttpClient client,
        string userName)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(userName);
        var tokenService = scope.ServiceProvider
            .GetRequiredService<TokenService>();
        var token = await tokenService.CreateAccessTokenAsync(user!);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.token);
    }

    private static async Task UpdateUserAsync(
        CustomWebApplicationFactory factory,
        string userName,
        Action<ApplicationUser> update)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(userName);
        update(user!);
        var result = await userManager.UpdateAsync(user!);
        result.Succeeded.Should().BeTrue();
    }
}
