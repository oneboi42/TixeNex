using System.Net;
using System.Net.Http.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TixeNex.Shared.Contracts.Audit;
using TixeNex.UI.Pages.Admin;

namespace TixeNex.UI.Tests.Admin;

public sealed class AuditLogPageTests : BunitContext
{
    [Theory]
    [InlineData("InProgress", "New", "InProgress → New")]
    [InlineData("New", "New", null)]
    public void AssignDetails_ShowNewAssigneeAndOnlyTheActualStatusTransition(
        string previousStatus,
        string newStatus,
        string? expectedTransition)
    {
        var details = $$"""
            {"Number":"HDH-1","Title":"Assignment audit test","PreviousAssignedToDisplayName":null,"AssignedToDisplayName":"New Agent","PreviousStatus":"{{previousStatus}}","NewStatus":"{{newStatus}}"}
            """;
        var cut = RenderPage("Assign", details);

        cut.WaitForAssertion(() =>
        {
            var lines = cut.FindAll("tbody tr td:last-child > div");
            lines[0].TextContent.Should().Contain("HDH-1 · \"Assignment audit test\"");
            lines[1].TextContent.Should().Contain("Assigned to New Agent");

            if (expectedTransition is null)
                lines[1].TextContent.Should().NotContain("→");
            else
                lines[1].TextContent.Should().Contain(expectedTransition);
        });
    }

    [Fact]
    public void ReassignDetails_ShowPreviousAndNewAssigneeInOrder()
    {
        const string details = """
            {"Number":"HDH-2","Title":"Reassignment audit test","PreviousAssignedToDisplayName":"Old Agent","AssignedToDisplayName":"New Agent","PreviousStatus":"New","NewStatus":"New"}
            """;
        var cut = RenderPage("Reassign", details);

        cut.WaitForAssertion(() =>
        {
            var lines = cut.FindAll("tbody tr td:last-child > div");
            lines[0].ClassList.Should().Contain("fw-medium");
            lines[1].ClassList.Should().Contain("text-body-secondary");
            lines[1].TextContent.Trim().Should().Be("Old Agent → New Agent");
            cut.Find("tbody details summary").ClassList.Should().Contain("text-muted");
        });
    }

    private IRenderedComponent<AuditLogPage> RenderPage(string action, string details)
    {
        var items = new[]
        {
            new AuditLogListItemDto
            {
                CreatedAtUtc = DateTime.UtcNow,
                Action = action,
                EntityName = "Ticket",
                EntityId = "1",
                PerformedBy = "admin",
                Details = details
            }
        };
        var client = new HttpClient(new JsonResponseHandler(items))
        {
            BaseAddress = new Uri("https://localhost/")
        };
        Services.AddSingleton<IHttpClientFactory>(new SingleClientFactory(client));

        return Render<AuditLogPage>();
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class JsonResponseHandler(IReadOnlyCollection<AuditLogListItemDto> items)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(items)
            });
    }
}
