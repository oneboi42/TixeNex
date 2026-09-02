using FluentAssertions;

namespace TixeNex.UI.Tests.Security;

public sealed class NginxSecurityHeadersConfigurationTests
{
    [Fact]
    public void Configuration_AddsAntiFramingAndBaselineSecurityHeaders()
    {
        var configuration = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "nginx.conf"));

        configuration.Should().Contain(
            "add_header Content-Security-Policy \"frame-ancestors 'none'\" always;");
        configuration.Should().Contain(
            "add_header X-Frame-Options \"DENY\" always;");
        configuration.Should().Contain(
            "add_header X-Content-Type-Options \"nosniff\" always;");
        configuration.Should().Contain(
            "add_header Referrer-Policy \"strict-origin-when-cross-origin\" always;");
        configuration.Should().Contain(
            "add_header Permissions-Policy \"camera=(), geolocation=(), microphone=()\" always;");
    }
}
