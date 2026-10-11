using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaudeCouncilManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class CloseTerminalOnTerminate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CloseTerminalOnTerminate",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloseTerminalOnTerminate",
                table: "Settings");
        }
    }
}
