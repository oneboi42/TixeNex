using System.Text.Json;
using TixeNex.Api.Application.Interfaces;
using TixeNex.Api.Domain;
using TixeNex.Api.Infrastructure.Persistence;

namespace TixeNex.Api.Application.Services;

public sealed class OutboxWriter : IOutboxWriter
{
    private readonly AppDbContext _db;

    public OutboxWriter(AppDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(string type, object payload, CancellationToken ct = default)
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow,
            Type = type,
            Payload = JsonSerializer.Serialize(payload),
            RetryCount = 0
        };

        _db.OutboxMessages.Add(message);

        return Task.CompletedTask;
    }
}
