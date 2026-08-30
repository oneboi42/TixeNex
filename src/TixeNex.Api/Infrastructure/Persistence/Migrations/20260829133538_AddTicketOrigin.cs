using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TixeNex.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "Tickets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Origin",
                table: "Tickets",
                column: "Origin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_Origin",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Tickets");
        }
    }
}
