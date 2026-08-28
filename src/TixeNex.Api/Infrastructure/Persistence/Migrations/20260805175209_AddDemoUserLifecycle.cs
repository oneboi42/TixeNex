using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TixeNex.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoUserLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DemoAbsoluteExpiresAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DemoExpiresAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemoUser",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_IsDemoUser_DemoExpiresAtUtc",
                table: "Users",
                columns: new[] { "IsDemoUser", "DemoExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_IsDemoUser_DemoExpiresAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DemoAbsoluteExpiresAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DemoExpiresAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsDemoUser",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastActivityAtUtc",
                table: "Users");
        }
    }
}
