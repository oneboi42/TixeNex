using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelpDeskHero.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExportMetadataAndScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "ExportJobs",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Format",
                table: "ExportJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ResourceType",
                table: "ExportJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Scope",
                table: "ExportJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "ExportJobs");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "ExportJobs");

            migrationBuilder.DropColumn(
                name: "ResourceType",
                table: "ExportJobs");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "ExportJobs");
        }
    }
}
