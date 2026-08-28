using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TixeNex.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageObjectName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StorageObjectName",
                table: "ExportJobs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageObjectName",
                table: "ExportJobs");
        }
    }
}
