## Background Jobs + Powiadomienia + Zalaczniki + Komentarze + CI/CD

Ta część rozwija projekt i przesuwa go w strone bardziej zywego systemu: nie tylko CRUD i auth, ale tez procesy w tle, komunikacja z uzytkownikiem, pliki, komentarze i podstawy pipeline'u.

Zakladam, ze masz juz:

- ASP.NET Identity
- JWT + refresh token
- role i policies
- audit log
- optimistic concurrency
- soft delete
- dashboard
- testy integracyjne i bUnit
- Blazor WebAssembly jako osobny klient

Celem tej czesci jest dodanie elementow, ktore sprawiaja, ze system zaczyna przypominac realny helpdesk, a nie tylko ladny CRUD w garniturze.

---

## Co dokladamy

1. Background jobs (np. Hangfire)
2. Powiadomienia email / webhook / in-app
3. Zalaczniki do ticketow
4. Komentarze i historia dyskusji
5. Pipeline CI/CD pod GitHub Actions
6. Upload i walidacja plikow
7. Odswiezanie dashboardu o dane "zywe"

---

## Docelowa struktura rozwiazania

```text
HelpDeskHero
├─ src
│  ├─ HelpDeskHero.Api
│  │  ├─ BackgroundJobs
│  │  ├─ Controllers
│  │  ├─ Domain
│  │  ├─ Infrastructure
│  │  │  ├─ Persistence
│  │  │  ├─ Notifications
│  │  │  └─ Storage
│  │  └─ Services
│  ├─ HelpDeskHero.UI
│  │  ├─ Pages
│  │  │  ├─ Tickets
│  │  │  └─ Notifications
│  │  ├─ Services
│  │  └─ Components
│  └─ HelpDeskHero.Shared
│     └─ Contracts
└─ .github
   └─ workflows
```

---

## 1. Background Jobs - dodanie Hangfire

Najprostsza droga edukacyjno-MVP: Hangfire z SQL Server jako storage. Daje scheduler, retry, dashboard i latwy model uruchamiania zadan.

### Pakiety

```powershell
dotnet add .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj package Hangfire.Core
dotnet add .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj package Hangfire.AspNetCore
dotnet add .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj package Hangfire.SqlServer
```

### `src/HelpDeskHero.Api/BackgroundJobs/Contracts/INotificationJob.cs`

```csharp
namespace HelpDeskHero.Api.BackgroundJobs.Contracts;

public interface INotificationJob
{
    Task SendTicketCreatedNotificationsAsync(int ticketId, CancellationToken ct = default);
    Task SendDailySummaryAsync(CancellationToken ct = default);
}
```

### `src/HelpDeskHero.Api/BackgroundJobs/NotificationJob.cs`

```csharp
using HelpDeskHero.Api.BackgroundJobs.Contracts;
using HelpDeskHero.Api.Infrastructure.Notifications;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.BackgroundJobs;

public sealed class NotificationJob : INotificationJob
{
    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;

    public NotificationJob(AppDbContext db, INotificationDispatcher dispatcher)
    {
        _db = db;
        _dispatcher = dispatcher;
    }

    public async Task SendTicketCreatedNotificationsAsync(int ticketId, CancellationToken ct = default)
    {
        var ticket = await _db.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == ticketId, ct);

        if (ticket is null)
            return;

        var subject = $"Nowe zgloszenie: {ticket.Number}";
        var body = $"Utworzono ticket {ticket.Number} - {ticket.Title}";

        await _dispatcher.DispatchAsync(new NotificationMessage
        {
            Channel = NotificationChannel.Email,
            Subject = subject,
            Body = body,
            UserId = ticket.CreatedByUserId
        }, ct);
    }

    public async Task SendDailySummaryAsync(CancellationToken ct = default)
    {
        var openCount = await _db.Tickets.CountAsync(x => !x.IsDeleted && x.Status != "Closed", ct);

        await _dispatcher.DispatchAsync(new NotificationMessage
        {
            Channel = NotificationChannel.Webhook,
            Subject = "HelpDeskHero - Daily Summary",
            Body = $"Open tickets: {openCount}"
        }, ct);
    }
}
```

### Rejestracja w `Program.cs`

```csharp
using Hangfire;
using Hangfire.SqlServer;
using HelpDeskHero.Api.BackgroundJobs;
using HelpDeskHero.Api.BackgroundJobs.Contracts;

builder.Services.AddHangfire(config =>
{
    config.UseSimpleAssemblyNameTypeSerializer()
          .UseRecommendedSerializerSettings()
          .UseSqlServerStorage(
              builder.Configuration.GetConnectionString("DefaultConnection"),
              new SqlServerStorageOptions
              {
                  PrepareSchemaIfNecessary = true
              });
});

builder.Services.AddHangfireServer();
builder.Services.AddScoped<INotificationJob, NotificationJob>();
```

W pipeline:

```csharp
app.UseHangfireDashboard("/hangfire");
RecurringJob.AddOrUpdate<INotificationJob>(
    "daily-summary",
    job => job.SendDailySummaryAsync(default),
    "0 7 * * *");
```

### Kolejkowanie zadania po utworzeniu ticketu

W `TicketsController` po zapisie:

```csharp
using Hangfire;

BackgroundJob.Enqueue<INotificationJob>(job =>
    job.SendTicketCreatedNotificationsAsync(ticket.Id, default));
```

---

## 2. Powiadomienia - model i dispatcher

Zamiast wciskac logike wysylki bezposrednio do kontrolera, lepiej zrobic prosty dispatcher z kanalami.

### `src/HelpDeskHero.Shared/Contracts/Notifications/NotificationDto.cs`

```csharp
namespace HelpDeskHero.Shared.Contracts.Notifications;

public sealed class NotificationDto
{
    public int Id { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Status { get; set; } = "Queued";
    public DateTime CreatedAtUtc { get; set; }
}
```

### `src/HelpDeskHero.Api/Infrastructure/Notifications/NotificationMessage.cs`

```csharp
namespace HelpDeskHero.Api.Infrastructure.Notifications;

public sealed class NotificationMessage
{
    public NotificationChannel Channel { get; set; } = NotificationChannel.Email;
    public string? UserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
```

### `src/HelpDeskHero.Api/Infrastructure/Notifications/NotificationChannel.cs`

```csharp
namespace HelpDeskHero.Api.Infrastructure.Notifications;

public enum NotificationChannel
{
    Email = 1,
    Webhook = 2,
    InApp = 3
}
```

### `src/HelpDeskHero.Api/Infrastructure/Notifications/INotificationSender.cs`

```csharp
namespace HelpDeskHero.Api.Infrastructure.Notifications;

public interface INotificationSender
{
    NotificationChannel Channel { get; }
    Task SendAsync(NotificationMessage message, CancellationToken ct = default);
}
```

### `src/HelpDeskHero.Api/Infrastructure/Notifications/INotificationDispatcher.cs`

```csharp
namespace HelpDeskHero.Api.Infrastructure.Notifications;

public interface INotificationDispatcher
{
    Task DispatchAsync(NotificationMessage message, CancellationToken ct = default);
}
```

### `src/HelpDeskHero.Api/Infrastructure/Notifications/NotificationDispatcher.cs`

```csharp
namespace HelpDeskHero.Api.Infrastructure.Notifications;

public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly IReadOnlyDictionary<NotificationChannel, INotificationSender> _senders;

    public NotificationDispatcher(IEnumerable<INotificationSender> senders)
    {
        _senders = senders.ToDictionary(x => x.Channel, x => x);
    }

    public async Task DispatchAsync(NotificationMessage message, CancellationToken ct = default)
    {
        if (!_senders.TryGetValue(message.Channel, out var sender))
            throw new InvalidOperationException($"Missing sender for channel {message.Channel}.");

        await sender.SendAsync(message, ct);
    }
}
```

### Przykladowe implementacje senderow

```csharp
public sealed class EmailNotificationSender : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        // Tu podlaczysz SMTP / SendGrid / Graph API
        return Task.CompletedTask;
    }
}

public sealed class WebhookNotificationSender : INotificationSender
{
    private readonly HttpClient _http;

    public WebhookNotificationSender(HttpClient http)
    {
        _http = http;
    }

    public NotificationChannel Channel => NotificationChannel.Webhook;

    public async Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        var payload = new { text = $"{message.Subject}: {message.Body}" };
        await _http.PostAsJsonAsync("https://example.invalid/webhook", payload, ct);
    }
}

public sealed class InAppNotificationSender : INotificationSender
{
    private readonly AppDbContext _db;

    public InAppNotificationSender(AppDbContext db)
    {
        _db = db;
    }

    public NotificationChannel Channel => NotificationChannel.InApp;

    public async Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        var entity = new UserNotification
        {
            UserId = message.UserId,
            Subject = message.Subject,
            Body = message.Body,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.UserNotifications.Add(entity);
        await _db.SaveChangesAsync(ct);
    }
}
```

### Rejestracja senderow

```csharp
builder.Services.AddHttpClient<WebhookNotificationSender>();
builder.Services.AddScoped<INotificationSender, EmailNotificationSender>();
builder.Services.AddScoped<INotificationSender, WebhookNotificationSender>();
builder.Services.AddScoped<INotificationSender, InAppNotificationSender>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
```

---

## 3. Zalaczniki - model danych i storage

- metadane w SQL Server
- plik fizycznie na dysku lub w storage
- rozmiar, MIME type i rozszerzenie walidowane po stronie API

### `src/HelpDeskHero.Api/Domain/TicketAttachment.cs`

```csharp
namespace HelpDeskHero.Api.Domain;

public sealed class TicketAttachment
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public Ticket Ticket { get; set; } = default!;

    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string RelativePath { get; set; } = string.Empty;

    public DateTime UploadedAtUtc { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
}
```

### `src/HelpDeskHero.Api/Infrastructure/Storage/IFileStorage.cs`

```csharp
namespace HelpDeskHero.Api.Infrastructure.Storage;

public interface IFileStorage
{
    Task<StoredFileResult> SaveAsync(IFormFile file, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default);
    Task DeleteAsync(string relativePath, CancellationToken ct = default);
}
```

### `src/HelpDeskHero.Api/Infrastructure/Storage/StoredFileResult.cs`

```csharp
namespace HelpDeskHero.Api.Infrastructure.Storage;

public sealed class StoredFileResult
{
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
```

### `src/HelpDeskHero.Api/Infrastructure/Storage/LocalFileStorage.cs`

```csharp
namespace HelpDeskHero.Api.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public LocalFileStorage(IWebHostEnvironment env, IConfiguration config)
    {
        _env = env;
        _config = config;
    }

    public async Task<StoredFileResult> SaveAsync(IFormFile file, CancellationToken ct = default)
    {
        var root = _config["FileStorage:RootPath"]
            ?? Path.Combine(_env.ContentRootPath, "App_Data", "attachments");

        Directory.CreateDirectory(root);

        var extension = Path.GetExtension(file.FileName);
        var safeName = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(root, safeName);

        await using var stream = File.Create(path);
        await file.CopyToAsync(stream, ct);

        return new StoredFileResult
        {
            OriginalFileName = file.FileName,
            StoredFileName = safeName,
            RelativePath = safeName,
            ContentType = file.ContentType,
            SizeBytes = file.Length
        };
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var root = _config["FileStorage:RootPath"]
            ?? Path.Combine(_env.ContentRootPath, "App_Data", "attachments");

        var path = Path.Combine(root, relativePath);
        Stream stream = File.OpenRead(path);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var root = _config["FileStorage:RootPath"]
            ?? Path.Combine(_env.ContentRootPath, "App_Data", "attachments");

        var path = Path.Combine(root, relativePath);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }
}
```

### Walidacja uploadu

Minimalne zasady:
- limit np. 10 MB
- biala lista rozszerzen
- biala lista MIME type
- blokada plikow `.exe`, `.ps1`, `.bat`, `.cmd`

Przykladowy helper:

```csharp
public static class AttachmentValidation
{
    private static readonly string[] AllowedExtensions = [".png", ".jpg", ".jpeg", ".pdf", ".txt", ".docx", ".xlsx"];
    private const long MaxSizeBytes = 10 * 1024 * 1024;

    public static void Validate(IFormFile file)
    {
        if (file.Length <= 0)
            throw new InvalidOperationException("Empty file.");

        if (file.Length > MaxSizeBytes)
            throw new InvalidOperationException("File too large.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException("File type not allowed.");
    }
}
```

---

## 4. Komentarze - model i endpointy

Bez komentarzy helpdesk jest jak ticket bez problemu: formalnie istnieje, ale nikt nie wie po co.

### `src/HelpDeskHero.Api/Domain/TicketComment.cs`

```csharp
namespace HelpDeskHero.Api.Domain;

public sealed class TicketComment
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public Ticket Ticket { get; set; } = default!;

    public string Body { get; set; } = string.Empty;
    public bool IsInternal { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByDisplayName { get; set; } = string.Empty;
}
```

### `src/HelpDeskHero.Shared/Contracts/Tickets/TicketCommentDto.cs`

```csharp
namespace HelpDeskHero.Shared.Contracts.Tickets;

public sealed class TicketCommentDto
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedByDisplayName { get; set; } = string.Empty;
}

public sealed class CreateTicketCommentDto
{
    public string Body { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
}
```

### `src/HelpDeskHero.Api/Controllers/TicketCommentsController.cs`

```csharp
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/tickets/{ticketId:int}/comments")]
[Authorize]
public sealed class TicketCommentsController : ControllerBase
{
    private readonly AppDbContext _db;

    public TicketCommentsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TicketCommentDto>>> GetAll(int ticketId, CancellationToken ct)
    {
        var items = await _db.TicketComments
            .AsNoTracking()
            .Where(x => x.TicketId == ticketId)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new TicketCommentDto
            {
                Id = x.Id,
                TicketId = x.TicketId,
                Body = x.Body,
                IsInternal = x.IsInternal,
                CreatedAtUtc = x.CreatedAtUtc,
                CreatedByDisplayName = x.CreatedByDisplayName
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<TicketCommentDto>> Create(
        int ticketId,
        CreateTicketCommentDto dto,
        CancellationToken ct)
    {
        var exists = await _db.Tickets.AnyAsync(x => x.Id == ticketId, ct);
        if (!exists)
            return NotFound();

        var entity = new TicketComment
        {
            TicketId = ticketId,
            Body = dto.Body.Trim(),
            IsInternal = dto.IsInternal,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = User.FindFirst("sub")?.Value ?? "unknown",
            CreatedByDisplayName = User.Identity?.Name ?? "unknown"
        };

        _db.TicketComments.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Ok(new TicketCommentDto
        {
            Id = entity.Id,
            TicketId = entity.TicketId,
            Body = entity.Body,
            IsInternal = entity.IsInternal,
            CreatedAtUtc = entity.CreatedAtUtc,
            CreatedByDisplayName = entity.CreatedByDisplayName
        });
    }
}
```

---

## 5. Attachment API - upload i download

### `src/HelpDeskHero.Shared/Contracts/Tickets/TicketAttachmentDto.cs`

```csharp
namespace HelpDeskHero.Shared.Contracts.Tickets;

public sealed class TicketAttachmentDto
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public string UploadedByUserId { get; set; } = string.Empty;
}
```

### `src/HelpDeskHero.Api/Controllers/TicketAttachmentsController.cs`

```csharp
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Storage;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/tickets/{ticketId:int}/attachments")]
[Authorize]
public sealed class TicketAttachmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;

    public TicketAttachmentsController(AppDbContext db, IFileStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TicketAttachmentDto>>> GetAll(int ticketId, CancellationToken ct)
    {
        var items = await _db.TicketAttachments
            .AsNoTracking()
            .Where(x => x.TicketId == ticketId)
            .OrderByDescending(x => x.UploadedAtUtc)
            .Select(x => new TicketAttachmentDto
            {
                Id = x.Id,
                TicketId = x.TicketId,
                OriginalFileName = x.OriginalFileName,
                ContentType = x.ContentType,
                SizeBytes = x.SizeBytes,
                UploadedAtUtc = x.UploadedAtUtc,
                UploadedByUserId = x.UploadedByUserId
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<TicketAttachmentDto>> Upload(
        int ticketId,
        IFormFile file,
        CancellationToken ct)
    {
        var exists = await _db.Tickets.AnyAsync(x => x.Id == ticketId, ct);
        if (!exists)
            return NotFound();

        AttachmentValidation.Validate(file);

        var stored = await _storage.SaveAsync(file, ct);

        var entity = new TicketAttachment
        {
            TicketId = ticketId,
            OriginalFileName = stored.OriginalFileName,
            StoredFileName = stored.StoredFileName,
            RelativePath = stored.RelativePath,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = User.FindFirst("sub")?.Value ?? "unknown"
        };

        _db.TicketAttachments.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Ok(new TicketAttachmentDto
        {
            Id = entity.Id,
            TicketId = entity.TicketId,
            OriginalFileName = entity.OriginalFileName,
            ContentType = entity.ContentType,
            SizeBytes = entity.SizeBytes,
            UploadedAtUtc = entity.UploadedAtUtc,
            UploadedByUserId = entity.UploadedByUserId
        });
    }

    [HttpGet("{attachmentId:int}/download")]
    public async Task<IActionResult> Download(int ticketId, int attachmentId, CancellationToken ct)
    {
        var item = await _db.TicketAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.TicketId == ticketId, ct);

        if (item is null)
            return NotFound();

        var stream = await _storage.OpenReadAsync(item.RelativePath, ct);
        return File(stream, item.ContentType, item.OriginalFileName);
    }
}
```

### Rejestracja storage

```csharp
using HelpDeskHero.Api.Infrastructure.Storage;

builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
```

---

## 6. Rozszerzenie modelu `Ticket`

W praktyce ticket powinien wiedziec o komentarzach i zalacznikach.

### fragment encji `Ticket`

```csharp
public ICollection<TicketComment> Comments { get; set; } = new List<TicketComment>();
public ICollection<TicketAttachment> Attachments { get; set; } = new List<TicketAttachment>();

public string CreatedByUserId { get; set; } = string.Empty;
public string? AssignedToUserId { get; set; }
```

### fragment `AppDbContext`

```csharp
public DbSet<TicketComment> TicketComments => Set<TicketComment>();
public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();
public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
```

Mapowanie:

```csharp
modelBuilder.Entity<TicketComment>(b =>
{
    b.ToTable("TicketComments");
    b.HasKey(x => x.Id);

    b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
    b.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
    b.Property(x => x.CreatedByDisplayName).HasMaxLength(200).IsRequired();

    b.HasOne(x => x.Ticket)
     .WithMany(x => x.Comments)
     .HasForeignKey(x => x.TicketId)
     .OnDelete(DeleteBehavior.Cascade);
});

modelBuilder.Entity<TicketAttachment>(b =>
{
    b.ToTable("TicketAttachments");
    b.HasKey(x => x.Id);

    b.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
    b.Property(x => x.StoredFileName).HasMaxLength(260).IsRequired();
    b.Property(x => x.RelativePath).HasMaxLength(520).IsRequired();
    b.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
    b.Property(x => x.UploadedByUserId).HasMaxLength(450).IsRequired();

    b.HasOne(x => x.Ticket)
     .WithMany(x => x.Attachments)
     .HasForeignKey(x => x.TicketId)
     .OnDelete(DeleteBehavior.Cascade);
});
```

Po zmianach wykonujesz:

```powershell
dotnet ef migrations add AddCommentsAttachmentsNotifications `
  --project .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj `
  --startup-project .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj

dotnet ef database update `
  --project .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj `
  --startup-project .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj
```

---

## 7. UI - komentarze i zalaczniki w Blazorze

### `src/HelpDeskHero.UI/Services/Api/TicketCommentApiClient.cs`

```csharp
using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Tickets;

namespace HelpDeskHero.UI.Services.Api;

public sealed class TicketCommentApiClient
{
    private readonly HttpClient _http;

    public TicketCommentApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<TicketCommentDto>> GetAllAsync(int ticketId, CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<TicketCommentDto>>(
            $"api/tickets/{ticketId}/comments", ct);

        return result ?? [];
    }

    public async Task<TicketCommentDto?> CreateAsync(
        int ticketId,
        CreateTicketCommentDto dto,
        CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"api/tickets/{ticketId}/comments", dto, ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TicketCommentDto>(cancellationToken: ct);
    }
}
```

### `src/HelpDeskHero.UI/Pages/Tickets/TicketComments.razor`

```razor
@using HelpDeskHero.Shared.Contracts.Tickets
@using HelpDeskHero.UI.Services.Api
@inject TicketCommentApiClient CommentApi

@if (_loading)
{
    <p>Ladowanie komentarzy...</p>
}
else if (_comments.Count == 0)
{
    <p>Brak komentarzy.</p>
}
else
{
    <ul class="list-group mb-3">
        @foreach (var item in _comments)
        {
            <li class="list-group-item">
                <div><strong>@item.CreatedByDisplayName</strong> (@item.CreatedAtUtc.ToLocalTime())</div>
                <div>@item.Body</div>
                @if (item.IsInternal)
                {
                    <small>Internal</small>
                }
            </li>
        }
    </ul>
}

<EditForm Model="_newComment" OnValidSubmit="HandleCreateAsync">
    <DataAnnotationsValidator />
    <ValidationSummary />

    <InputTextArea class="form-control mb-2" @bind-Value="_newComment.Body" />
    <div class="form-check mb-2">
        <InputCheckbox class="form-check-input" @bind-Value="_newComment.IsInternal" />
        <label class="form-check-label">Komentarz wewnetrzny</label>
    </div>
    <button class="btn btn-primary" type="submit">Dodaj komentarz</button>
</EditForm>

@code {
    [Parameter] public int TicketId { get; set; }

    private bool _loading = true;
    private List<TicketCommentDto> _comments = new();
    private CreateTicketCommentDto _newComment = new();

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        _comments = (await CommentApi.GetAllAsync(TicketId)).ToList();
        _loading = false;
    }

    private async Task HandleCreateAsync()
    {
        var created = await CommentApi.CreateAsync(TicketId, _newComment);
        if (created is not null)
        {
            _comments.Add(created);
            _newComment = new CreateTicketCommentDto();
        }
    }
}
```

### Upload zalacznika z Blazora

Do uploadu uzyj `InputFile` i `MultipartFormDataContent`.

### `src/HelpDeskHero.UI/Services/Api/TicketAttachmentApiClient.cs`

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Components.Forms;

namespace HelpDeskHero.UI.Services.Api;

public sealed class TicketAttachmentApiClient
{
    private readonly HttpClient _http;

    public TicketAttachmentApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<TicketAttachmentDto>> GetAllAsync(int ticketId, CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<TicketAttachmentDto>>(
            $"api/tickets/{ticketId}/attachments", ct);

        return result ?? [];
    }

    public async Task<TicketAttachmentDto?> UploadAsync(
        int ticketId,
        IBrowserFile file,
        long maxFileSize = 10 * 1024 * 1024,
        CancellationToken ct = default)
    {
        await using var stream = file.OpenReadStream(maxFileSize, ct);

        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);

        fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        content.Add(fileContent, "file", file.Name);

        var response = await _http.PostAsync(
            $"api/tickets/{ticketId}/attachments", content, ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TicketAttachmentDto>(cancellationToken: ct);
    }
}
```

---

## 8. UI - in-app notifications

Dodaj prosty endpoint do pobierania notyfikacji uzytkownika i pokaz badge w menu.

### `src/HelpDeskHero.Api/Domain/UserNotification.cs`

```csharp
namespace HelpDeskHero.Api.Domain;

public sealed class UserNotification
{
    public int Id { get; set; }
    public string? UserId { get; set; }

    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}
```

### `src/HelpDeskHero.Shared/Contracts/Notifications/UserNotificationDto.cs`

```csharp
namespace HelpDeskHero.Shared.Contracts.Notifications;

public sealed class UserNotificationDto
{
    public int Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
```

### `src/HelpDeskHero.Api/Controllers/NotificationsController.cs`

```csharp
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<UserNotificationDto>>> GetMine(CancellationToken ct)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var items = await _db.UserNotifications
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(20)
            .Select(x => new UserNotificationDto
            {
                Id = x.Id,
                Subject = x.Subject,
                Body = x.Body,
                IsRead = x.IsRead,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkAsRead(int id, CancellationToken ct)
    {
        var userId = User.FindFirst("sub")?.Value;
        var item = await _db.UserNotifications
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

        if (item is null)
            return NotFound();

        item.IsRead = true;
        item.ReadAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}
```

---

## 9. CI/CD - GitHub Actions

Na start prosty pipeline:
- restore
- build
- test
- publish API
- publish UI

### `.github/workflows/build.yml`

```yaml
name: build-and-test

on:
  push:
    branches: [ "main" ]
  pull_request:
    branches: [ "main" ]

jobs:
  build:
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore .\HelpDeskHero.sln

      - name: Build
        run: dotnet build .\HelpDeskHero.sln --configuration Release --no-restore

      - name: Test
        run: dotnet test .\HelpDeskHero.sln --configuration Release --no-build

      - name: Publish API
        run: dotnet publish .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj -c Release -o .\artifacts\api

      - name: Publish UI
        run: dotnet publish .\src\HelpDeskHero.UI\HelpDeskHero.UI.csproj -c Release -o .\artifacts\ui

      - name: Upload API artifact
        uses: actions/upload-artifact@v4
        with:
          name: helpdeskhero-api
          path: .\artifacts\api

      - name: Upload UI artifact
        uses: actions/upload-artifact@v4
        with:
          name: helpdeskhero-ui
          path: .\artifacts\ui
```

### Dalszy etap pipeline'u

Potem mozesz dolozyc:
- migracje uruchamiane warunkowo
- deploy na IIS / App Service / VM
- smoke tests
- skan zaleznosci
- osobny workflow dla release

---

## 10. Walidacja i bezpieczenstwo przy nowych funkcjach

### Dla komentarzy
- limit dlugosci, np. 4000 znakow
- brak pustych tresci po `Trim()`
- opcjonalnie blokada HTML albo sanitizacja

### Dla zalacznikow
- biala lista rozszerzen
- limit rozmiaru
- storage poza `wwwroot`
- pobieranie tylko przez autoryzowany endpoint
- logowanie upload / download w audycie

### Dla jobs
- zadania maja byc idempotentne, gdzie to mozliwe
- retry z glowa, nie w nieskonczonosc
- nie wrzucaj do joba calej encji EF, tylko ID

Bo serializowanie calego obiektu z nawigacjami do joba to jest proszenie sie o balagan,balagan zwykle wygrywa.

---

## 11. Kolejnosc wdrozenia

Najrozsadniejsza kolejnosc:

1. Dodaj modele: komentarze, zalaczniki, notyfikacje
2. Rozszerz `AppDbContext`
3. Zrob migracje
4. Dodaj `IFileStorage` + lokalny storage
5. Dodaj endpointy comments i attachments
6. Dodaj klientow API w Blazorze
7. Osadz komentarze i zalaczniki na ekranie szczegolow ticketu
8. Dodaj Hangfire i pierwszy background job
9. Dodaj `NotificationsController` i badge w UI
10. Doloz pipeline GitHub Actions

Tak zrobisz to warstwami.

---

## 12. Checklista testowa

### API
- mozna dodac komentarz do ticketu
- nie mozna dodac komentarza do nieistniejacego ticketu
- mozna wyslac plik PDF/JPG/TXT
- nie mozna wyslac pliku niedozwolonego typu
- nie mozna wyslac pliku powyzej limitu
- mozna pobrac zalacznik po autoryzacji
- po utworzeniu ticketu kolejkuje sie job powiadomienia
- endpoint notyfikacji zwraca tylko dane zalogowanego usera

### UI
- komentarze laduja sie na stronie szczegolow
- mozna dodac nowy komentarz
- upload pokazuje nowy plik na liscie
- notyfikacje pokazuja sie w menu / panelu
- bledy uploadu sa czytelnie wyswietlane

### CI/CD
- build przechodzi na czystym runnerze
- testy przechodza
- artifact API i UI sa publikowane

---

## 13. Co dalej

Najbardziej sensowny kolejny etap to:

- SignalR dla live updates
- kolejka outbox / inbox
- thumbnail generation dla obrazow
- wersjonowanie zalacznikow
- SLA i eskalacje
- automatyczne przypisania
- raporty i export PDF
- deployment na IIS / Azure / Docker


---

## 14. Mapa "co jest edukacyjne, a co produkcyjne"

### Edukacyjne 
- lokalny storage plikow
- prosty Hangfire dashboard
- podstawowy dispatcher powiadomien
- notyfikacje in-app w jednej tabeli
- prosty pipeline build/test/publish

### Produkcyjne do rozbudowy
- storage typu Azure Blob / S3 / MinIO
- antywirus / skan plikow
- webhook signing / retry policy / dead-letter
- rate limiting dla uploadow
- lepszy model retention dla notyfikacji
- observability (metryki, logi, tracing)

---

## 15. Podsumowanie

Ta część wnosi do HelpDeskHero 5 bardzo waznych warstw:

- automatyzacje w tle
- realne powiadomienia
- pliki
- rozmowe przy tickecie
- pipeline do budowania i testowania

Nastepny naturalny pakiet:
**SignalR + live updates + SLA + eskalacje + automatyczne przypisania + outbox pattern**
