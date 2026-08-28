using System.Security.Claims;
using FluentAssertions;
using TixeNex.Api.Application.TicketVisibility;
using TixeNex.Api.Controllers;
using TixeNex.Api.Domain;
using TixeNex.Api.Infrastructure.Persistence;
using TixeNex.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace TixeNex.Api.IntegrationTests.Tickets;

public sealed class TicketAttachmentQuotaTests
{
    [Fact]
    public async Task Upload_WithinUploaderQuota_SavesFileAndRecord()
    {
        await using var db = CreateContext();
        var ticket = await AddTicketAsync(db, "caller");
        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "caller");

        var result = await controller.Upload(ticket.Id, CreateFile(1024), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        storage.SaveCallCount.Should().Be(1);
        (await db.TicketAttachments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Upload_ExceedingUploaderQuota_DoesNotSaveFileOrRecord()
    {
        await using var db = CreateContext();
        var ticket = await AddTicketAsync(db, "caller");
        db.TicketAttachments.Add(new TicketAttachment
        {
            TicketId = ticket.Id,
            OriginalFileName = "existing.txt",
            StoredFileName = "existing.txt",
            RelativePath = "existing.txt",
            ContentType = "text/plain",
            SizeBytes = AttachmentValidation.MaxTotalSizeBytesPerUser,
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = "caller"
        });
        await db.SaveChangesAsync();

        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "caller");

        var result = await controller.Upload(ticket.Id, CreateFile(1), CancellationToken.None);

        var rejected = result.Result.Should().BeOfType<ObjectResult>().Subject;
        rejected.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        storage.SaveCallCount.Should().Be(0);
        (await db.TicketAttachments.CountAsync()).Should().Be(1);
    }

    private static TicketAttachmentsController CreateController(
        AppDbContext db,
        RecordingStorage storage,
        string userId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, "User")
        };
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };

        return new TicketAttachmentsController(
            db,
            storage,
            new TicketVisibilityContextResolver())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static async Task<Ticket> AddTicketAsync(AppDbContext db, string requesterUserId)
    {
        var ticket = new Ticket
        {
            Number = "HDH-ATTACHMENT",
            Title = "Attachment quota test",
            Description = "Test ticket",
            Priority = "Medium",
            Status = "New",
            CreatedAtUtc = DateTime.UtcNow,
            RequesterUserId = requesterUserId
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    private static IFormFile CreateFile(int sizeBytes) => new FormFile(
        new MemoryStream(new byte[sizeBytes]),
        0,
        sizeBytes,
        "file",
        "attachment.txt")
    {
        Headers = new HeaderDictionary(),
        ContentType = "text/plain"
    };

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"attachment-quota-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private sealed class RecordingStorage : IFileStorage
    {
        public int SaveCallCount { get; private set; }

        public Task<StoredFileResult> SaveAsync(IFormFile file, CancellationToken ct = default)
        {
            SaveCallCount++;
            return Task.FromResult(new StoredFileResult
            {
                OriginalFileName = file.FileName,
                StoredFileName = "stored.txt",
                RelativePath = "stored.txt",
                ContentType = file.ContentType,
                SizeBytes = file.Length
            });
        }

        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string relativePath, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
