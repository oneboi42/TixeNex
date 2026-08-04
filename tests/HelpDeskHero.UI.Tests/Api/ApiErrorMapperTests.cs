using System.Net;
using System.Text;
using FluentAssertions;
using HelpDeskHero.UI.Services.Api;

namespace HelpDeskHero.UI.Tests.Api;

public sealed class ApiErrorMapperTests
{
    [Theory]
    [InlineData("concurrency_conflict")]
    [InlineData("closed_ticket_cannot_be_assigned")]
    public async Task GetProblemCodeAsync_ReadsProblemDetailsExtension(string code)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent($$"""{"code":"{{code}}"}""", Encoding.UTF8, "application/problem+json")
        };

        (await ApiErrorMapper.GetProblemCodeAsync(response)).Should().Be(code);
    }
}
