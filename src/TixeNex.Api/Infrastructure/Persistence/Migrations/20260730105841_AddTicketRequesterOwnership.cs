using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TixeNex.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketRequesterOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequesterUserId",
                table: "Tickets",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_AssignedToUserId",
                table: "Tickets",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_RequesterUserId",
                table: "Tickets",
                column: "RequesterUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_Users_RequesterUserId",
                table: "Tickets",
                column: "RequesterUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_Users_RequesterUserId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_AssignedToUserId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_RequesterUserId",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "RequesterUserId",
                table: "Tickets");
        }
    }
}
