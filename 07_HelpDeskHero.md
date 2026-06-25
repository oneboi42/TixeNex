## SignalR + live updates + SLA + eskalacje + automatyczne przypisania + outbox pattern


Zakres:
- live updates w UI przez SignalR
- liczenie SLA i deadline'ow
- eskalacje automatyczne
- automatyczne przypisania ticketow
- outbox pattern do bezpiecznego publikowania zdarzen
- background jobs do kontroli SLA i wysylek powiadomien

---

## 1. Architektura po rozbudowie

Docelowy przeplyw:

1. Uzytkownik tworzy lub edytuje ticket.
2. API zapisuje zmiane w bazie.
3. API zapisuje zdarzenie domenowe do tabeli OutboxMessages.
4. Hangfire lub BackgroundService przetwarza outbox.
5. Zdarzenie jest publikowane:
   - do SignalR Hub (live refresh),
   - do systemu powiadomien,
   - do logu audytowego / webhooka / emaila.
6. Job SLA sprawdza opoznione tickety i uruchamia eskalacje.
7. UI odswieza liste i szczegoly bez recznego F5.

To jest zdrowy model, bo oddziela:
- zapis transakcyjny,
- publikacje zdarzen,
- aktualizacje UI,
- procesy asynchroniczne.

Nie robimy tu "wywolaj wszystko od razu po save". To zwykle konczy sie plątanina callbackow i smutkiem administratora.

---

## 2. Nowe encje i tabele

### 2.1 `TicketSlaPolicy`

Przechowuje definicje SLA, np. ile czasu ma ticket typu High, Medium, Low.

Przykladowe pola:
- `Id`
- `Name`
- `Priority`
- `FirstResponseMinutes`
- `ResolveMinutes`
- `IsActive`

### 2.2 `TicketEscalation`

Rejestruje eskalacje wykonane dla ticketu.

Przykladowe pola:
- `Id`
- `TicketId`
- `EscalationLevel`
- `TriggeredAtUtc`
- `Reason`
- `AssignedToUserId`
- `NotificationSent`

### 2.3 `OutboxMessage`

Serce outbox pattern.

Przykladowe pola:
- `Id` (Guid)
- `OccurredAtUtc`
- `Type`
- `Payload`
- `ProcessedAtUtc`
- `Error`
- `RetryCount`

### 2.4 Rozszerzenia `Ticket`

Do encji `Ticket` dodaj:
- `DueFirstResponseAtUtc`
- `DueResolveAtUtc`
- `FirstRespondedAtUtc`
- `ResolvedAtUtc`
- `AssignedToUserId`
- `EscalationLevel`
- `LastNotifiedAtUtc`

---

## 3. Encje - przykladowy kod

### `Domain/TicketSlaPolicy.cs`

```csharp
namespace HelpDeskHero.Api.Domain;

public sealed class TicketSlaPolicy
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Priority { get; set; } = "Medium";
    public int FirstResponseMinutes { get; set; }
    public int ResolveMinutes { get; set; }
    public bool IsActive { get; set; } = true;
}
```

### `Domain/TicketEscalation.cs`

```csharp
namespace HelpDeskHero.Api.Domain;

public sealed class TicketEscalation
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public Ticket Ticket { get; set; } = default!;

    public int EscalationLevel { get; set; }
    public DateTime TriggeredAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? AssignedToUserId { get; set; }
    public bool NotificationSent { get; set; }
}
```

### `Domain/OutboxMessage.cs`

```csharp
namespace HelpDeskHero.Api.Domain;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime? ProcessedAtUtc { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }
}
```

---

## 4. Rozszerzenie `Ticket`

### `Domain/Ticket.cs`

Dopisujesz pola:

```csharp
public DateTime? DueFirstResponseAtUtc { get; set; }
public DateTime? DueResolveAtUtc { get; set; }
public DateTime? FirstRespondedAtUtc { get; set; }
public DateTime? ResolvedAtUtc { get; set; }
public string? AssignedToUserId { get; set; }
public int EscalationLevel { get; set; }
public DateTime? LastNotifiedAtUtc { get; set; }
```

Opcjonalnie dodaj navigation do usera, jesli trzymasz Identity:

```csharp
public AppUser? AssignedToUser { get; set; }
```

---

## 5. DbContext - nowe DbSety

### `Infrastructure/Persistence/AppDbContext.cs`

```csharp
public DbSet<TicketSlaPolicy> TicketSlaPolicies => Set<TicketSlaPolicy>();
public DbSet<TicketEscalation> TicketEscalations => Set<TicketEscalation>();
public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
```

W `OnModelCreating`:

```csharp
modelBuilder.Entity<TicketSlaPolicy>(b =>
{
    b.ToTable("TicketSlaPolicies");
    b.HasKey(x => x.Id);

    b.Property(x => x.Name).HasMaxLength(100).IsRequired();
    b.Property(x => x.Priority).HasMaxLength(30).IsRequired();
    b.Property(x => x.FirstResponseMinutes).IsRequired();
    b.Property(x => x.ResolveMinutes).IsRequired();
    b.Property(x => x.IsActive).IsRequired();
});

modelBuilder.Entity<TicketEscalation>(b =>
{
    b.ToTable("TicketEscalations");
    b.HasKey(x => x.Id);

    b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
    b.Property(x => x.TriggeredAtUtc).IsRequired();

    b.HasOne(x => x.Ticket)
        .WithMany()
        .HasForeignKey(x => x.TicketId)
        .OnDelete(DeleteBehavior.Cascade);
});

modelBuilder.Entity<OutboxMessage>(b =>
{
    b.ToTable("OutboxMessages");
    b.HasKey(x => x.Id);

    b.Property(x => x.Type).HasMaxLength(200).IsRequired();
    b.Property(x => x.Payload).IsRequired();
    b.Property(x => x.OccurredAtUtc).IsRequired();
    b.Property(x => x.RetryCount).IsRequired();
});
```

Dla `Ticket` dodaj konfiguracje nowych pol.

---

## 6. Shared - nowe kontrakty DTO

Dodaj do `HelpDeskHero.Shared` kontrakty pod live updates i SLA.

### `Contracts/Tickets/TicketLiveUpdateDto.cs`

```csharp
namespace HelpDeskHero.Shared.Contracts.Tickets;

public sealed class TicketLiveUpdateDto
{
    public int TicketId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string? AssignedToUserId { get; set; }
    public int EscalationLevel { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}
```

### `Contracts/Tickets/TicketSlaDto.cs`

```csharp
namespace HelpDeskHero.Shared.Contracts.Tickets;

public sealed class TicketSlaDto
{
    public int TicketId { get; set; }
    public DateTime? DueFirstResponseAtUtc { get; set; }
    public DateTime? DueResolveAtUtc { get; set; }
    public bool FirstResponseBreached { get; set; }
    public bool ResolveBreached { get; set; }
    public int EscalationLevel { get; set; }
}
```

---

## 7. SignalR - live updates

ASP.NET Core SignalR sluzy do pushowania aktualizacji do klienta w czasie rzeczywistym. To jest sensowne dla list ticketow, szczegolow, komentarzy i notyfikacji. Nie trzeba wtedy odswiezac strony jak w 2009 roku.

### Pakiet

Jesli trzeba, dodaj pakiet po stronie UI:

```powershell
# zwykle w UI
 dotnet add .\src\HelpDeskHero.UI\HelpDeskHero.UI.csproj package Microsoft.AspNetCore.SignalR.Client
```

### `Hubs/TicketsHub.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HelpDeskHero.Api.Hubs;

[Authorize]
public sealed class TicketsHub : Hub
{
    public async Task JoinDashboard()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "dashboard");
    }

    public async Task JoinTicket(string ticketId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"ticket:{ticketId}");
    }

    public async Task LeaveTicket(string ticketId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ticket:{ticketId}");
    }
}
```

### Rejestracja w `Program.cs`

```csharp
builder.Services.AddSignalR();
```

I mapowanie:

```csharp
app.MapHub<TicketsHub>("/hubs/tickets");
```

---

## 8. Serwis do publikowania live updates

### `Application/Interfaces/ITicketLiveNotifier.cs`

```csharp
using HelpDeskHero.Shared.Contracts.Tickets;

namespace HelpDeskHero.Api.Application.Interfaces;

public interface ITicketLiveNotifier
{
    Task NotifyTicketChangedAsync(TicketLiveUpdateDto dto, CancellationToken ct = default);
}
```

### `Infrastructure/Notifications/SignalRTicketLiveNotifier.cs`

```csharp
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Hubs;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.SignalR;

namespace HelpDeskHero.Api.Infrastructure.Notifications;

public sealed class SignalRTicketLiveNotifier : ITicketLiveNotifier
{
    private readonly IHubContext<TicketsHub> _hubContext;

    public SignalRTicketLiveNotifier(IHubContext<TicketsHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyTicketChangedAsync(TicketLiveUpdateDto dto, CancellationToken ct = default)
    {
        await _hubContext.Clients.Group("dashboard")
            .SendAsync("TicketChanged", dto, ct);

        await _hubContext.Clients.Group($"ticket:{dto.TicketId}")
            .SendAsync("TicketChanged", dto, ct);
    }
}
```

Rejestracja:

```csharp
builder.Services.AddScoped<ITicketLiveNotifier, SignalRTicketLiveNotifier>();
```

---

## 9. SLA - liczenie terminow

### `Application/Interfaces/ISlaCalculator.cs`

```csharp
namespace HelpDeskHero.Api.Application.Interfaces;

public interface ISlaCalculator
{
    Task ApplySlaAsync(Ticket ticket, CancellationToken ct = default);
}
```

### `Application/Services/SlaCalculator.cs`

```csharp
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Application.Services;

public sealed class SlaCalculator : ISlaCalculator
{
    private readonly AppDbContext _db;

    public SlaCalculator(AppDbContext db)
    {
        _db = db;
    }

    public async Task ApplySlaAsync(Ticket ticket, CancellationToken ct = default)
    {
        var policy = await _db.TicketSlaPolicies
            .AsNoTracking()
            .Where(x => x.IsActive && x.Priority == ticket.Priority)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (policy is null)
            return;

        var created = ticket.CreatedAtUtc == default ? DateTime.UtcNow : ticket.CreatedAtUtc;

        ticket.DueFirstResponseAtUtc = created.AddMinutes(policy.FirstResponseMinutes);
        ticket.DueResolveAtUtc = created.AddMinutes(policy.ResolveMinutes);
    }
}
```

Rejestracja:

```csharp
builder.Services.AddScoped<ISlaCalculator, SlaCalculator>();
```

---

## 10. Automatyczne przypisania ticketow

Logika auto-assignment moze byc prosta na start:
- round-robin,
- najmniej aktywnych ticketow,
- tylko dla userow z rola `Agent`.

### `Application/Interfaces/ITicketAssignmentService.cs`

```csharp
namespace HelpDeskHero.Api.Application.Interfaces;

public interface ITicketAssignmentService
{
    Task<string?> AssignAsync(Ticket ticket, CancellationToken ct = default);
}
```

### `Application/Services/TicketAssignmentService.cs`

```csharp
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Application.Services;

public sealed class TicketAssignmentService : ITicketAssignmentService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly AppDbContext _db;

    public TicketAssignmentService(UserManager<AppUser> userManager, AppDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<string?> AssignAsync(Ticket ticket, CancellationToken ct = default)
    {
        var agents = await _userManager.GetUsersInRoleAsync("Agent");
        if (agents.Count == 0)
            return null;

        var agentLoads = new List<(string UserId, int Count)>();

        foreach (var agent in agents)
        {
            var activeCount = await _db.Tickets.CountAsync(
                x => x.AssignedToUserId == agent.Id
                  && x.Status != "Closed"
                  && !x.IsDeleted,
                ct);

            agentLoads.Add((agent.Id, activeCount));
        }

        var selected = agentLoads.OrderBy(x => x.Count).First();
        ticket.AssignedToUserId = selected.UserId;

        return selected.UserId;
    }
}
```

To jest prosty wariant. Produkcyjnie mozna dodac skill matrix, zespoly, godziny pracy i kolejki. 

---

## 11. Outbox pattern - zapis zdarzen

Outbox pattern sprawia, ze zdarzenia do publikacji zapisujesz w tej samej transakcji co zmiana biznesowa. Potem osobny worker je przetwarza.

### `Application/Interfaces/IOutboxWriter.cs`

```csharp
namespace HelpDeskHero.Api.Application.Interfaces;

public interface IOutboxWriter
{
    Task AddAsync(string type, object payload, CancellationToken ct = default);
}
```

### `Application/Services/OutboxWriter.cs`

```csharp
using System.Text.Json;
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;

namespace HelpDeskHero.Api.Application.Services;

public sealed class OutboxWriter : IOutboxWriter
{
    private readonly AppDbContext _db;

    public OutboxWriter(AppDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(string type, object payload, CancellationToken ct = default)
    {
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow,
            Type = type,
            Payload = JsonSerializer.Serialize(payload),
            RetryCount = 0
        };

        _db.OutboxMessages.Add(msg);
        return Task.CompletedTask;
    }
}
```

Rejestracja:

```csharp
builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();
```

---

## 12. Przetwarzanie outbox

Mozesz to robic przez:
- Hangfire recurring job,
- `BackgroundService`,
- Quartz.

Dla spojnosc z poprzednimi pakietami zostaniemy przy Hangfire albo prostym serwisie.

### `Infrastructure/Background/OutboxProcessorService.cs`

```csharp
using System.Text.Json;
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Infrastructure.Background;

public sealed class OutboxProcessorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxProcessorService> _logger;

    public OutboxProcessorService(IServiceProvider serviceProvider, ILogger<OutboxProcessorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notifier = scope.ServiceProvider.GetRequiredService<ITicketLiveNotifier>();

                var messages = await db.OutboxMessages
                    .Where(x => x.ProcessedAtUtc == null && x.RetryCount < 10)
                    .OrderBy(x => x.OccurredAtUtc)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                foreach (var msg in messages)
                {
                    try
                    {
                        if (msg.Type == "TicketChanged")
                        {
                            var dto = JsonSerializer.Deserialize<TicketLiveUpdateDto>(msg.Payload);
                            if (dto is not null)
                            {
                                await notifier.NotifyTicketChangedAsync(dto, stoppingToken);
                            }
                        }

                        msg.ProcessedAtUtc = DateTime.UtcNow;
                        msg.Error = null;
                    }
                    catch (Exception ex)
                    {
                        msg.RetryCount += 1;
                        msg.Error = ex.Message;
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox processing failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

Rejestracja:

```csharp
builder.Services.AddHostedService<OutboxProcessorService>();
```

---

## 13. SLA watchdog i eskalacje

Potrzebny jest job, ktory cyklicznie sprawdza tickety po deadline.

### `Application/Interfaces/ISlaMonitorService.cs`

```csharp
namespace HelpDeskHero.Api.Application.Interfaces;

public interface ISlaMonitorService
{
    Task CheckBreachesAsync(CancellationToken ct = default);
}
```

### `Application/Services/SlaMonitorService.cs`

```csharp
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Application.Services;

public sealed class SlaMonitorService : ISlaMonitorService
{
    private readonly AppDbContext _db;
    private readonly IOutboxWriter _outboxWriter;

    public SlaMonitorService(AppDbContext db, IOutboxWriter outboxWriter)
    {
        _db = db;
        _outboxWriter = outboxWriter;
    }

    public async Task CheckBreachesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var breached = await _db.Tickets
            .Where(x => !x.IsDeleted)
            .Where(x => x.Status != "Closed")
            .Where(x => x.DueResolveAtUtc != null && x.DueResolveAtUtc < now)
            .ToListAsync(ct);

        foreach (var ticket in breached)
        {
            var newLevel = ticket.EscalationLevel + 1;
            ticket.EscalationLevel = newLevel;
            ticket.LastNotifiedAtUtc = now;

            _db.TicketEscalations.Add(new TicketEscalation
            {
                TicketId = ticket.Id,
                EscalationLevel = newLevel,
                TriggeredAtUtc = now,
                Reason = "Resolve SLA breached.",
                AssignedToUserId = ticket.AssignedToUserId,
                NotificationSent = false
            });

            await _outboxWriter.AddAsync("TicketChanged", new TicketLiveUpdateDto
            {
                TicketId = ticket.Id,
                EventType = "SlaBreached",
                Status = ticket.Status,
                Priority = ticket.Priority,
                AssignedToUserId = ticket.AssignedToUserId,
                EscalationLevel = ticket.EscalationLevel,
                ChangedAtUtc = now
            }, ct);
        }

        await _db.SaveChangesAsync(ct);
    }
}
```

Jesli uzywasz Hangfire, dodaj recurring job co 5 minut.

---

## 14. Integracja z `TicketsController`

Przy tworzeniu ticketu:
- wylicz SLA,
- zrob auto-assignment,
- zapisz zdarzenie do outbox.

Przyklad szkicu w `POST`:

```csharp
await _slaCalculator.ApplySlaAsync(ticket, ct);
await _ticketAssignmentService.AssignAsync(ticket, ct);

_db.Tickets.Add(ticket);

await _outboxWriter.AddAsync("TicketChanged", new TicketLiveUpdateDto
{
    TicketId = ticket.Id,
    EventType = "Created",
    Status = ticket.Status,
    Priority = ticket.Priority,
    AssignedToUserId = ticket.AssignedToUserId,
    EscalationLevel = ticket.EscalationLevel,
    ChangedAtUtc = DateTime.UtcNow
}, ct);

await _db.SaveChangesAsync(ct);
```

Przy `PUT` i zmianie statusu / przypisania robisz to samo: zapis do outbox z `EventType = "Updated"`, `"Assigned"`, `"Closed"`, itd.

Wazna rzecz: jesli potrzebujesz ID ticketa przed outbox payload, najpierw zrob `SaveChangesAsync`, potem dodaj outbox i drugi `SaveChangesAsync`, albo generuj zdarzenie po tym jak EF uzupelni ID. Drobiazg, ale bez niego potrafi wejsc komedia omylek.

---

## 15. UI - klient SignalR

### `Services/Realtime/TicketsRealtimeClient.cs`

```csharp
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.SignalR.Client;

namespace HelpDeskHero.UI.Services.Realtime;

public sealed class TicketsRealtimeClient : IAsyncDisposable
{
    private readonly NavigationManager _navigationManager;
    private HubConnection? _connection;

    public event Func<TicketLiveUpdateDto, Task>? OnTicketChanged;

    public TicketsRealtimeClient(NavigationManager navigationManager)
    {
        _navigationManager = navigationManager;
    }

    public async Task StartAsync(string accessToken, CancellationToken ct = default)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(_navigationManager.ToAbsoluteUri("https://localhost:5001/hubs/tickets"), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(accessToken)!;
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<TicketLiveUpdateDto>("TicketChanged", async dto =>
        {
            if (OnTicketChanged is not null)
                await OnTicketChanged(dto);
        });

        await _connection.StartAsync(ct);
        await _connection.SendAsync("JoinDashboard", ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
```

Rejestracja:

```csharp
builder.Services.AddScoped<TicketsRealtimeClient>();
```

---

## 16. UI - live refresh listy ticketow

W komponencie listy bindowanie na event z SignalR:

### szkic `Pages/Tickets/TicketListPage.razor`

```razor
@implements IAsyncDisposable
@inject TicketsRealtimeClient Realtime
@inject TokenStorageService TokenStorage

@code {
    protected override async Task OnInitializedAsync()
    {
        Realtime.OnTicketChanged += HandleTicketChangedAsync;

        var token = await TokenStorage.GetAccessTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            await Realtime.StartAsync(token);
        }

        await LoadAsync();
    }

    private async Task HandleTicketChangedAsync(TicketLiveUpdateDto dto)
    {
        await LoadAsync();
        await InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        Realtime.OnTicketChanged -= HandleTicketChangedAsync;
        await Realtime.DisposeAsync();
    }
}
```

Mozesz tez zamiast pelnego `LoadAsync()` robic lokalna aktualizacje jednego rekordu. To jest szybsze, ale na start mniej czytelne.

---

## 17. UI - SLA badges i live status

Na liscie ticketow dodaj kolumny:
- `Assigned To`
- `Due Resolve`
- `Escalation`
- `SLA`

Przyklad logiki badge:

```razor
@if (ticket.DueResolveAtUtc is not null && ticket.DueResolveAtUtc < DateTime.UtcNow)
{
    <span class="badge bg-danger">SLA breached</span>
}
else
{
    <span class="badge bg-success">On time</span>
}
```

Dla `EscalationLevel > 0` pokazuj np. zolta / czerwona etykiete.

---

## 18. Powiadomienia i eskalacja menedzera

Gdy `EscalationLevel` rośnie:
- wysylasz email do team leadera,
- tworzysz in-app notification,
- opcjonalnie odpalasz webhook do Teams / Slack.

Mozesz wykorzystac juz istniejacy serwis powiadomien z pakietu 6. Tu po prostu dodajesz nowy trigger: `SlaBreached`.

Praktyczny model:
- level 1 -> agent + assigned owner
- level 2 -> team leader
- level 3 -> manager / admin

---

## 19. Seed danych startowych

Przygotuj seed SLA i role:

### Przykladowe polityki SLA

- `Low`: first response 240 min, resolve 2880 min
- `Medium`: first response 60 min, resolve 480 min
- `High`: first response 15 min, resolve 120 min

Przykladowe role:
- `Admin`
- `Manager`
- `Agent`
- `User`

Przykladowi agenci:
- `agent1@helpdeskhero.local`
- `agent2@helpdeskhero.local`

---

## 20. Migracje

Po dodaniu nowych encji i kolumn:

```powershell
dotnet ef migrations add AddSignalRSlaEscalationOutbox `
  --project .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj `
  --startup-project .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj
```

```powershell
dotnet ef database update `
  --project .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj `
  --startup-project .\src\HelpDeskHero.Api\HelpDeskHero.Api.csproj
```

---

## 21. `Program.cs` - rejestracja uslug

Przykladowy zestaw:

```csharp
builder.Services.AddSignalR();
builder.Services.AddScoped<ITicketLiveNotifier, SignalRTicketLiveNotifier>();
builder.Services.AddScoped<ISlaCalculator, SlaCalculator>();
builder.Services.AddScoped<ISlaMonitorService, SlaMonitorService>();
builder.Services.AddScoped<ITicketAssignmentService, TicketAssignmentService>();
builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();
builder.Services.AddHostedService<OutboxProcessorService>();
```

Mapowanie huba:

```csharp
app.MapHub<TicketsHub>("/hubs/tickets");
```

Jesli UI laczy sie z innego originu, pamietaj o CORS dla SignalR i API. Bez tego przegladarka zrobi widowiskowe "nie".

---

## 22. Checklista wdrozeniowa

### Backend
- dodane encje `TicketSlaPolicy`, `TicketEscalation`, `OutboxMessage`
- rozszerzona encja `Ticket`
- migracja wykonana
- `TicketsHub` zmapowany
- SignalR dodany do services
- outbox worker dziala
- SLA monitor dziala cyklicznie
- automatyczne przypisanie dziala dla roli `Agent`

### UI
- klient SignalR podpiety
- lista ticketow odswieza sie live
- szczegoly ticketu odswiezaja sie live
- badge SLA widoczny
- poziom eskalacji widoczny
- reconnect dziala po odswiezeniu / utracie polaczenia

### Testy
- create ticket -> pojawia sie live update
- edit ticket -> lista aktualizuje status
- SLA breach -> rosnie `EscalationLevel`
- outbox message dostaje `ProcessedAtUtc`
- auto-assignment przypisuje najmniej obciazonego agenta

---

## 23. Co warto dopracowac potem

To jest bardzo solidny etap, ale produkcyjnie warto dodac jeszcze:
- deduplikacje zdarzen w outbox,
- idempotentnych konsumentow,
- wygaszanie / archiwizacje outbox,
- metryki i health check dla SignalR,
- harmonogramy pracy agentow,
- SLA zalezne od kategorii, nie tylko priorytetu,
- suppress powiadomien, zeby nie spamowac przy kazdej drobnej zmianie,
- rate limiting i ograniczenia na hubie.

---

## 24. Rekomendowana kolejnosc wdrazania

1. Dodaj encje i migracje.
2. Dodaj SLA calculator.
3. Dodaj auto-assignment.
4. Dodaj outbox writer.
5. Dodaj worker przetwarzajacy outbox.
6. Dodaj SignalR Hub i notifier.
7. Podepnij klienta SignalR w UI.
8. Dodaj job SLA monitorujacy opoznienia.
9. Dodaj eskalacje i powiadomienia managera.
10. Dopracuj testy i retry policy.

To jest dobra kolejnosc, bo nie wrzucasz od razu pieciu ruchomych elementow do jednego gara i nie udajesz, ze to strategia. 

---

## 25. Podsumowanie

Po tej częsci  HelpDeskHero zyskuje:
- live updates bez recznego odswiezania,
- realne SLA,
- automatyczne eskalacje,
- przypisywanie agentow,
- bezpieczniejsze publikowanie zdarzen przez outbox,
- sensowny kierunek pod bardziej dojrzaly system ticketowy.


---

## 26. Propozycja kolejnego etapu

Naturalny pakiet 8:
- multi-tenant / organizacje / dzialy
- kategorie i workflow per ticket type
- knowledge base i suggested solutions
- raporty i KPI agentow
- event bus / message broker (np. RabbitMQ)
- webhooks dla integracji z zewnetrznymi systemami
