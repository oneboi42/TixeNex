using System.Data.Common;
using FluentAssertions;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HelpDeskHero.Worker.Tests.Exports;

public sealed class ExportJobClaimTests
{
    [Fact]
    public async Task ClaimPendingJob_ClaimsOnceAndDatabasePredicateExcludesNonPendingJobs()
    {
        var interceptor = new ClaimCommandInterceptor(1, 0);
        await using var db = CreateDbContext(interceptor);
        var jobId = Guid.NewGuid();

        var firstClaim = await ExportService.ClaimPendingJobAsync(
            db,
            jobId,
            CancellationToken.None);
        var duplicateClaim = await ExportService.ClaimPendingJobAsync(
            db,
            jobId,
            CancellationToken.None);

        firstClaim.Should().BeTrue();
        duplicateClaim.Should().BeFalse();
        interceptor.Commands.Should().HaveCount(2);
        interceptor.Commands.Should().OnlyContain(command =>
            command.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) &&
            command.Contains("Status", StringComparison.OrdinalIgnoreCase) &&
            command.Contains("= 0", StringComparison.Ordinal));
        interceptor.ParameterValues.Should().OnlyContain(values =>
            values.Contains(1));
    }

    [Fact]
    public async Task ClaimPendingJob_DoesNotUpdateStoredOwnerOrScope()
    {
        var interceptor = new ClaimCommandInterceptor(1);
        await using var db = CreateDbContext(interceptor);

        await ExportService.ClaimPendingJobAsync(
            db,
            Guid.NewGuid(),
            CancellationToken.None);

        var command = interceptor.Commands.Single();
        command.Contains("UserId", StringComparison.OrdinalIgnoreCase)
            .Should().BeFalse();
        command.Contains("Scope", StringComparison.OrdinalIgnoreCase)
            .Should().BeFalse();
        command.Contains("ResourceType", StringComparison.OrdinalIgnoreCase)
            .Should().BeFalse();
        command.Contains("Format", StringComparison.OrdinalIgnoreCase)
            .Should().BeFalse();
    }

    private static AppDbContext CreateDbContext(
        ClaimCommandInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(
                "Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true")
            .AddInterceptors(
                new SuppressConnectionInterceptor(),
                interceptor)
            .Options;

        return new AppDbContext(options);
    }

    private sealed class ClaimCommandInterceptor(params int[] results)
        : DbCommandInterceptor
    {
        private readonly Queue<int> _results = new(results);

        public List<string> Commands { get; } = [];
        public List<List<object>> ParameterValues { get; } = [];

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            ParameterValues.Add(
                command.Parameters
                    .Cast<DbParameter>()
                    .Select(parameter => parameter.Value)
                    .Where(value => value is not null)
                    .ToList()!);

            return ValueTask.FromResult(
                InterceptionResult<int>.SuppressWithResult(_results.Dequeue()));
        }
    }

    private sealed class SuppressConnectionInterceptor
        : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }
}
