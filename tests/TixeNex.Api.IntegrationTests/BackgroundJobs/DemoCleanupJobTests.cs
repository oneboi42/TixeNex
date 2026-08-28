using FluentAssertions;
using TixeNex.Api.Application.Interfaces;
using TixeNex.Api.BackgroundJobs;
using TixeNex.Api.Domain;
using TixeNex.Api.Infrastructure.Persistence;
using TixeNex.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace TixeNex.Api.IntegrationTests.BackgroundJobs;

public sealed class DemoCleanupJobTests
{
    [Fact]
    public async Task CleanupExpiredDemoData_DeletesExpiredTicketGraphAndAttachmentFile()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;

        var requester = User(
            id: "demo-requester",
            isDemoWorkspace: true,
            isDemoUser: true,
            absoluteExpiry: now.AddHours(1));

        db.Users.Add(requester);

        db.Tickets.AddRange(
            new Ticket
            {
                Id = 1,
                Number = "HDH-CLEANUP-1",
                Title = "Expired demo ticket",
                Description = "Should be physically removed",
                Status = "New",
                Priority = "Medium",
                CreatedAtUtc = now.AddHours(-1),
                RequesterUserId = requester.Id,
                DemoExpiresAtUtc = now.AddMinutes(-1)
            },
            new Ticket
            {
                Id = 2,
                Number = "HDH-NORMAL-1",
                Title = "Normal ticket",
                Description = "Must survive cleanup",
                Status = "New",
                Priority = "Medium",
                CreatedAtUtc = now.AddHours(-1),
                RequesterUserId = requester.Id,
                DemoExpiresAtUtc = null
            });

        db.TicketComments.Add(new TicketComment
        {
            TicketId = 1,
            Body = "Expired demo comment",
            CreatedAtUtc = now.AddMinutes(-10),
            CreatedByUserId = requester.Id,
            CreatedByDisplayName = requester.DisplayName
        });

        db.TicketAttachments.Add(new TicketAttachment
        {
            TicketId = 1,
            OriginalFileName = "cleanup-test.txt",
            StoredFileName = "stored-cleanup-test.txt",
            RelativePath = "stored-cleanup-test.txt",
            ContentType = "text/plain",
            SizeBytes = 10,
            UploadedAtUtc = now.AddMinutes(-10),
            UploadedByUserId = requester.Id
        });

        await db.SaveChangesAsync();

        var fileStorage = new RecordingFileStorage();
        var exportStorage = new RecordingExportObjectStorage();

        await CreateJob(db, fileStorage, exportStorage)
            .CleanupExpiredDemoDataAsync();

        (await db.Tickets
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Id == 1))
            .Should().BeFalse();

        (await db.TicketComments
                .IgnoreQueryFilters()
                .AnyAsync(x => x.TicketId == 1))
            .Should().BeFalse();

        (await db.TicketAttachments
                .IgnoreQueryFilters()
                .AnyAsync(x => x.TicketId == 1))
            .Should().BeFalse();

        (await db.Tickets
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Id == 2))
            .Should().BeTrue();

        fileStorage.DeletedPaths.Should()
            .ContainSingle()
            .Which.Should().Be("stored-cleanup-test.txt");

        exportStorage.DeletedObjects.Should().BeEmpty();
    }

    [Fact]
    public async Task CleanupExpiredDemoData_DeletesExpiredTemporaryUserAndOwnedTransientData()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;

        var expiredUser = User(
            id: "expired-demo-user",
            isDemoWorkspace: true,
            isDemoUser: true,
            absoluteExpiry: now.AddMinutes(-1));

        db.Users.Add(expiredUser);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = expiredUser.Id,
            TokenHash = "EXPIRED-DEMO-REFRESH-TOKEN-HASH",
            DeviceName = "Cleanup test",
            CreatedAtUtc = now.AddHours(-2),
            ExpiresAtUtc = now.AddHours(1)
        });

        db.UserNotifications.Add(new UserNotification
        {
            UserId = expiredUser.Id,
            Subject = "Cleanup notification",
            Body = "Should be removed",
            IsRead = false,
            CreatedAtUtc = now.AddMinutes(-20)
        });

        db.AuditLogs.Add(new AuditLog
        {
            CreatedAtUtc = now.AddMinutes(-20),
            Action = "Test",
            EntityName = "Demo",
            EntityId = "expired-demo-user",
            UserId = expiredUser.Id,
            UserName = expiredUser.UserName
        });

        var exportId = Guid.NewGuid();
        db.ExportJobs.Add(new ExportJob
        {
            Id = exportId,
            UserId = expiredUser.Id,
            Status = ExportStatus.Completed,
            CreatedAt = now.AddHours(-1),
            CompletedAt = now.AddMinutes(-30),
            FileName = "expired-demo-export.csv",
            StorageObjectName = "exports/expired-demo-export.csv",
            ResourceType = ExportResourceType.Tickets,
            Format = ExportFormat.Csv,
            Scope = ExportScope.Own
        });

        await db.SaveChangesAsync();

        var fileStorage = new RecordingFileStorage();
        var exportStorage = new RecordingExportObjectStorage();

        await CreateJob(db, fileStorage, exportStorage)
            .CleanupExpiredDemoDataAsync();

        (await db.Users.AnyAsync(x => x.Id == expiredUser.Id))
            .Should().BeFalse();

        (await db.RefreshTokens.AnyAsync(x => x.UserId == expiredUser.Id))
            .Should().BeFalse();

        (await db.UserNotifications.AnyAsync(x => x.UserId == expiredUser.Id))
            .Should().BeFalse();

        (await db.AuditLogs.AnyAsync(x => x.UserId == expiredUser.Id))
            .Should().BeFalse();

        (await db.ExportJobs.AnyAsync(x => x.Id == exportId))
            .Should().BeFalse();

        exportStorage.DeletedObjects.Should()
            .ContainSingle()
            .Which.Should().Be("exports/expired-demo-export.csv");

        fileStorage.DeletedPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task CleanupExpiredDemoData_PreservesPersistentDemoAgentsActiveDemoUsersAndNormalData()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;

        var persistentDemoAgent = User(
            id: "demo-agent-1",
            isDemoWorkspace: true,
            isDemoUser: false,
            absoluteExpiry: null);

        var activeDemoUser = User(
            id: "active-demo-user",
            isDemoWorkspace: true,
            isDemoUser: true,
            absoluteExpiry: now.AddHours(1));

        var normalUser = User(
            id: "normal-user",
            isDemoWorkspace: false,
            isDemoUser: false,
            absoluteExpiry: null);

        db.Users.AddRange(
            persistentDemoAgent,
            activeDemoUser,
            normalUser);

        db.Tickets.AddRange(
            new Ticket
            {
                Id = 10,
                Number = "HDH-ACTIVE-DEMO",
                Title = "Active demo ticket",
                Description = "Must survive cleanup",
                Status = "New",
                Priority = "Medium",
                CreatedAtUtc = now,
                RequesterUserId = activeDemoUser.Id,
                DemoExpiresAtUtc = now.AddHours(1)
            },
            new Ticket
            {
                Id = 11,
                Number = "HDH-NORMAL",
                Title = "Normal ticket",
                Description = "Must survive cleanup",
                Status = "New",
                Priority = "Medium",
                CreatedAtUtc = now,
                RequesterUserId = normalUser.Id
            });

        await db.SaveChangesAsync();

        var fileStorage = new RecordingFileStorage();
        var exportStorage = new RecordingExportObjectStorage();

        await CreateJob(db, fileStorage, exportStorage)
            .CleanupExpiredDemoDataAsync();

        (await db.Users.AnyAsync(x => x.Id == persistentDemoAgent.Id))
            .Should().BeTrue();

        (await db.Users.AnyAsync(x => x.Id == activeDemoUser.Id))
            .Should().BeTrue();

        (await db.Users.AnyAsync(x => x.Id == normalUser.Id))
            .Should().BeTrue();

        (await db.Tickets.IgnoreQueryFilters().CountAsync())
            .Should().Be(2);

        fileStorage.DeletedPaths.Should().BeEmpty();
        exportStorage.DeletedObjects.Should().BeEmpty();
    }

    [Fact]
    public async Task CleanupExpiredDemoData_ClearsReferencesToExpiredTemporaryUserFromSurvivingTicket()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;

        var expiredAgent = User(
            id: "expired-demo-agent",
            isDemoWorkspace: true,
            isDemoUser: true,
            absoluteExpiry: now.AddMinutes(-1));

        var activeRequester = User(
            id: "active-requester",
            isDemoWorkspace: true,
            isDemoUser: true,
            absoluteExpiry: now.AddHours(1));

        db.Users.AddRange(expiredAgent, activeRequester);

        db.Tickets.Add(new Ticket
        {
            Id = 20,
            Number = "HDH-SURVIVING-DEMO",
            Title = "Surviving ticket",
            Description = "Assignee expires before this ticket",
            Status = "New",
            Priority = "Medium",
            CreatedAtUtc = now,
            RequesterUserId = activeRequester.Id,
            AssignedToUserId = expiredAgent.Id,
            DemoExpiresAtUtc = now.AddHours(1)
        });

        await db.SaveChangesAsync();

        await CreateJob(
                db,
                new RecordingFileStorage(),
                new RecordingExportObjectStorage())
            .CleanupExpiredDemoDataAsync();

        var survivingTicket = await db.Tickets
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == 20);

        survivingTicket.AssignedToUserId.Should().BeNull();
        survivingTicket.RequesterUserId.Should().Be(activeRequester.Id);
        (await db.Users.AnyAsync(x => x.Id == expiredAgent.Id))
            .Should().BeFalse();
    }

    [Fact]
    public async Task CleanupExpiredDemoData_CanRunTwiceWithoutDeletingExternalObjectsTwice()
    {
        await using var db = CreateContext();
        var now = DateTime.UtcNow;

        var expiredUser = User(
            id: "expired-idempotent-user",
            isDemoWorkspace: true,
            isDemoUser: true,
            absoluteExpiry: now.AddMinutes(-1));

        db.Users.Add(expiredUser);

        db.Tickets.Add(new Ticket
        {
            Id = 30,
            Number = "HDH-IDEMPOTENT",
            Title = "Idempotent cleanup",
            Description = "Should only delete external objects once",
            Status = "New",
            Priority = "Medium",
            CreatedAtUtc = now.AddMinutes(-10),
            RequesterUserId = expiredUser.Id,
            DemoExpiresAtUtc = now.AddMinutes(-1)
        });

        db.TicketAttachments.Add(new TicketAttachment
        {
            TicketId = 30,
            OriginalFileName = "idempotent.txt",
            StoredFileName = "idempotent.txt",
            RelativePath = "idempotent.txt",
            ContentType = "text/plain",
            SizeBytes = 1,
            UploadedAtUtc = now.AddMinutes(-5),
            UploadedByUserId = expiredUser.Id
        });

        db.ExportJobs.Add(new ExportJob
        {
            Id = Guid.NewGuid(),
            UserId = expiredUser.Id,
            Status = ExportStatus.Completed,
            CreatedAt = now.AddMinutes(-5),
            CompletedAt = now.AddMinutes(-4),
            FileName = "idempotent.csv",
            StorageObjectName = "exports/idempotent.csv",
            ResourceType = ExportResourceType.Tickets,
            Format = ExportFormat.Csv,
            Scope = ExportScope.Own
        });

        await db.SaveChangesAsync();

        var fileStorage = new RecordingFileStorage();
        var exportStorage = new RecordingExportObjectStorage();
        var job = CreateJob(db, fileStorage, exportStorage);

        await job.CleanupExpiredDemoDataAsync();
        await job.CleanupExpiredDemoDataAsync();

        fileStorage.DeletedPaths.Should().Equal("idempotent.txt");
        exportStorage.DeletedObjects.Should().Equal("exports/idempotent.csv");
    }

    [Fact]
    public async Task NormalTicketDeletion_StillUsesSoftDelete()
    {
        await using var db = CreateContext();

        var ticket = new Ticket
        {
            Id = 40,
            Number = "HDH-SOFT-DELETE",
            Title = "Normal deletion",
            Description = "Normal application deletes must remain soft deletes",
            Status = "New",
            Priority = "Medium",
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        db.Tickets.Remove(ticket);
        await db.SaveChangesAsync();

        (await db.Tickets.AnyAsync(x => x.Id == 40))
            .Should().BeFalse();

        var storedTicket = await db.Tickets
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == 40);

        storedTicket.IsDeleted.Should().BeTrue();
        storedTicket.DeletedAtUtc.Should().NotBeNull();
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"demo-cleanup-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private static DemoCleanupJob CreateJob(
        AppDbContext db,
        RecordingFileStorage fileStorage,
        RecordingExportObjectStorage exportStorage)
    {
        return new DemoCleanupJob(
            db,
            fileStorage,
            exportStorage,
            NullLogger<DemoCleanupJob>.Instance);
    }

    private static ApplicationUser User(
        string id,
        bool isDemoWorkspace,
        bool isDemoUser,
        DateTime? absoluteExpiry)
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            Email = $"{id}@test.local",
            NormalizedEmail = $"{id}@test.local".ToUpperInvariant(),
            DisplayName = id,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-2),
            IsDemoWorkspace = isDemoWorkspace,
            IsDemoUser = isDemoUser,
            DemoExpiresAtUtc = isDemoUser
                ? DateTime.UtcNow.AddMinutes(-1)
                : null,
            DemoAbsoluteExpiresAtUtc = absoluteExpiry,
            LastActivityAtUtc = isDemoUser
                ? DateTime.UtcNow.AddHours(-1)
                : null
        };
    }

    private sealed class RecordingFileStorage : IFileStorage
    {
        public List<string> DeletedPaths { get; } = [];

        public Task<StoredFileResult> SaveAsync(
            IFormFile file,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(
            string relativePath,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string relativePath,
            CancellationToken ct = default)
        {
            DeletedPaths.Add(relativePath);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingExportObjectStorage
        : IExportObjectStorage
    {
        public List<string> DeletedObjects { get; } = [];

        public Task<byte[]> DownloadAsync(
            string objectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<byte>());

        public Task DeleteAsync(
            string objectName,
            CancellationToken cancellationToken = default)
        {
            DeletedObjects.Add(objectName);
            return Task.CompletedTask;
        }
    }
}