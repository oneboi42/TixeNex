using System.Text;
using FluentAssertions;
using TixeNex.Worker.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TixeNex.Worker.Tests.Messaging;

public sealed class ExportMessageHandlerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"ExportJobId\":null}")]
    [InlineData("{\"ExportJobId\":42}")]
    [InlineData("{\"ExportJobId\":\"not-a-guid\"}")]
    public async Task HandleAsync_AcknowledgesUnusableMessages(string payload)
    {
        var processed = 0;
        var acknowledged = 0;
        var retried = 0;

        await HandleAsync(
            payload,
            _ =>
            {
                processed++;
                return Task.CompletedTask;
            },
            () => acknowledged++,
            () => retried++);

        processed.Should().Be(0);
        acknowledged.Should().Be(1);
        retried.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_PoisonMessageDoesNotBlockLaterValidMessage()
    {
        var jobId = Guid.NewGuid();
        var processedIds = new List<Guid>();
        var acknowledged = 0;
        var retried = 0;

        await HandleAsync(
            "{broken",
            id =>
            {
                processedIds.Add(id);
                return Task.CompletedTask;
            },
            () => acknowledged++,
            () => retried++);
        await HandleAsync(
            $"{{\"ExportJobId\":\"{jobId}\"}}",
            id =>
            {
                processedIds.Add(id);
                return Task.CompletedTask;
            },
            () => acknowledged++,
            () => retried++);

        processedIds.Should().Equal(jobId);
        acknowledged.Should().Be(2);
        retried.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ProcessingFailureIsRetriedWithoutAcknowledgement()
    {
        var acknowledged = 0;
        var retried = 0;

        await HandleAsync(
            $"{{\"ExportJobId\":\"{Guid.NewGuid()}\"}}",
            _ => throw new InvalidOperationException("processing failed"),
            () => acknowledged++,
            () => retried++);

        acknowledged.Should().Be(0);
        retried.Should().Be(1);
    }

    private static Task HandleAsync(
        string payload,
        Func<Guid, Task> process,
        Action acknowledge,
        Action retry) =>
        ExportMessageHandler.HandleAsync(
            Encoding.UTF8.GetBytes(payload),
            process,
            () =>
            {
                acknowledge();
                return Task.CompletedTask;
            },
            () =>
            {
                retry();
                return Task.CompletedTask;
            },
            NullLogger.Instance);
}
