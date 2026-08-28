using TixeNex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TixeNex.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260714140000_AddTicketCreator")]
public sealed class AddTicketCreator : Migration
{
    // This migration ID may already be present in deployed databases. Keep it as a no-op so
    // their migration history remains valid while ticket ownership is sourced from AuditLogs.
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
