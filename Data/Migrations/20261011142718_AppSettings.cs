using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaudeCouncilManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    SessionHost = table.Column<int>(type: "INTEGER", nullable: false),
                    PowerShellPath = table.Column<string>(type: "TEXT", nullable: true),
                    CustomProgram = table.Column<string>(type: "TEXT", nullable: false),
                    CustomArguments = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
