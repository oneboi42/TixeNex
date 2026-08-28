using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TixeNex.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoTicketLifetime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DemoExpiresAtUtc",
                table: "Tickets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_DemoExpiresAtUtc",
                table: "Tickets",
                column: "DemoExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_DemoExpiresAtUtc",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "DemoExpiresAtUtc",
                table: "Tickets");
        }
    }
}
