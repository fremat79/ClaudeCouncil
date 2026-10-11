using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaudeCouncilManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class Favorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "Experts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "Councils",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "Experts");

            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "Councils");
        }
    }
}
