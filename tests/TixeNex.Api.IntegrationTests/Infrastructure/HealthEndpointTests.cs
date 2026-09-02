using System.Net;

namespace TixeNex.Api.IntegrationTests.Infrastructure;

[Collection("ApiIntegration")]
public sealed class HealthEndpointTests(
    CustomWebApplicationFactory factory)
{
    [Fact]
    public async Task Live_ReturnsOnlyAnEmptySuccessResponse()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_DoesNotExposeDependencyDiagnostics()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Contains(
            response.StatusCode,
            new[]
            {
                HttpStatusCode.OK,
                HttpStatusCode.ServiceUnavailable
            });
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }
}
