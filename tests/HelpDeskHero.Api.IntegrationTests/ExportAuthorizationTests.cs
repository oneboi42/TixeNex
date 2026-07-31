using System.Security.Claims;
using FluentAssertions;
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Application.Services.Exports;
using HelpDeskHero.Api.Controllers;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Exports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.IntegrationTests;

public sealed class ExportAuthorizationTests
{
    [Theory]
    [InlineData("User", "Own")]
    [InlineData("Agent", "Own")]
    [InlineData("Agent", "Assigned")]
    [InlineData("Admin", "All")]
    public async Task CreateExport_AllowsRoleScopeCombinations(
        string role,
        string scope)
    {
        await using var db = CreateDbContext();
        var publisher = new RecordingPublisher();
        var controller = CreateController(db, publisher, "caller", role);

        var result = await controller.CreateExport(
            Request(scope),
            CancellationToken.None);

        result.Result.Should().BeOfType<AcceptedResult>();
        var job = await db.ExportJobs.SingleAsync();
        job.UserId.Should().Be("caller");
        job.ResourceType.Should().Be(ExportResourceType.Tickets);
        job.Format.Should().Be(ExportFormat.Csv);
        job.Scope.ToString().Should().Be(scope);
        publisher.Message.Should().Be(new ExportRequested(job.Id, job.UserId));
    }

    [Theory]
    [InlineData("User", "Assigned")]
    [InlineData("User", "All")]
    [InlineData("Agent", "All")]
    [InlineData("Admin", "Own")]
    [InlineData("Admin", "Assigned")]
    public async Task CreateExport_ForbidsRoleScopeCombinations(
        string role,
        string scope)
    {
        await using var db = CreateDbContext();
        var publisher = new RecordingPublisher();
        var controller = CreateController(db, publisher, "caller", role);

        var result = await controller.CreateExport(
            Request(scope),
            CancellationToken.None);

        result.Result.Should().BeOfType<ForbidResult>();
        (await db.ExportJobs.AnyAsync()).Should().BeFalse();
        publisher.Message.Should().BeNull();
    }

    [Theory]
    [InlineData("Users", "Csv", "Own", "ResourceType")]
    [InlineData("Tickets", "Pdf", "Own", "Format")]
    [InlineData("Tickets", "Csv", "Everything", "Scope")]
    [InlineData("0", "Csv", "Own", "ResourceType")]
    public async Task CreateExport_RejectsUnsupportedValues(
        string resourceType,
        string format,
        string scope,
        string errorKey)
    {
        await using var db = CreateDbContext();
        var controller = CreateController(db, new RecordingPublisher(), "caller", "User");

        var result = await controller.CreateExport(
            new CreateExportRequestDto
            {
                ResourceType = resourceType,
                Format = format,
                Scope = scope
            },
            CancellationToken.None);

        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var details = badRequest.Value.Should().BeOfType<ValidationProblemDetails>().Subject;
        details.Errors.Should().ContainKey(errorKey);
        details.Extensions["code"].Should().Be("validation_error");
    }

    [Theory]
    [InlineData("User", "Own")]
    [InlineData("Agent", "Own,Assigned")]
    [InlineData("Admin", "All")]
    public void GetOptions_ReturnsRoleSpecificScopes(
        string role,
        string expectedScopes)
    {
        using var db = CreateDbContext();
        var controller = CreateController(db, new RecordingPublisher(), "caller", role);

        var result = controller.GetOptions();

        var options = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<ExportOptionsDto>().Subject;
        options.AllowedResourceTypes.Should().Equal("Tickets");
        options.AllowedFormats.Should().Equal("Csv");
        options.AllowedScopes.Should().Equal(expectedScopes.Split(','));
    }

    [Fact]
    public void GetOptions_AdminRoleTakesPrecedenceOverAgentRole()
    {
        using var db = CreateDbContext();
        var controller = CreateController(
            db,
            new RecordingPublisher(),
            "caller",
            "Agent",
            "Admin");

        var result = controller.GetOptions();

        var options = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<ExportOptionsDto>().Subject;
        options.AllowedScopes.Should().Equal("All");
    }

    [Fact]
    public void GetOptions_WithoutNameIdentifier_ReturnsUnauthorized()
    {
        using var db = CreateDbContext();
        var controller = CreateController(db, new RecordingPublisher(), null, "User");

        controller.GetOptions().Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public void GetOptions_WithUnsupportedRole_ReturnsForbidden()
    {
        using var db = CreateDbContext();
        var controller = CreateController(db, new RecordingPublisher(), "caller", "Manager");

        controller.GetOptions().Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetMyExports_ReturnsStoredMetadata()
    {
        await using var db = CreateDbContext();
        db.ExportJobs.Add(new ExportJob
        {
            Id = Guid.NewGuid(),
            UserId = "caller",
            Status = ExportStatus.Failed,
            CreatedAt = DateTime.UtcNow,
            ResourceType = ExportResourceType.Tickets,
            Format = ExportFormat.Csv,
            Scope = ExportScope.Assigned,
            ErrorMessage = "Export failed"
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, new RecordingPublisher(), "caller", "Agent");

        var result = await controller.GetMyExports(CancellationToken.None);

        var jobs = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<List<ExportJobDto>>().Subject;
        jobs.Should().ContainSingle();
        jobs[0].ResourceType.Should().Be("Tickets");
        jobs[0].Format.Should().Be("Csv");
        jobs[0].Scope.Should().Be("Assigned");
        jobs[0].ErrorMessage.Should().Be("Export failed");
    }

    private static CreateExportRequestDto Request(string scope) => new()
    {
        ResourceType = "Tickets",
        Format = "Csv",
        Scope = scope
    };

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ExportAuthorizationTests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static ExportsController CreateController(
        AppDbContext db,
        RecordingPublisher publisher,
        string? userId,
        params string[] roles)
    {
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role)).ToList();
        if (userId is not null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));

        var controller = new ExportsController(
            db,
            publisher,
            new StubExportObjectStorage())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
                }
            }
        };

        return controller;
    }

    private sealed class RecordingPublisher : IMessagePublisher
    {
        public object? Message { get; private set; }

        public Task PublishAsync<T>(T message) where T : class
        {
            Message = message;
            return Task.CompletedTask;
        }
    }

    private sealed class StubExportObjectStorage : IExportObjectStorage
    {
        public Task<byte[]> DownloadAsync(
            string objectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<byte>());
    }
}
