using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaudeCouncilManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class CouncilWorkingFolderAndSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkingFolder",
                table: "Councils",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CouncilSessions",
                columns: table => new
                {
                    CouncilId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpertId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouncilSessions", x => new { x.CouncilId, x.ExpertId });
                    table.ForeignKey(
                        name: "FK_CouncilSessions_Councils_CouncilId",
                        column: x => x.CouncilId,
                        principalTable: "Councils",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CouncilSessions_Experts_ExpertId",
                        column: x => x.ExpertId,
                        principalTable: "Experts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CouncilSessions_ExpertId",
                table: "CouncilSessions",
                column: "ExpertId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CouncilSessions");

            migrationBuilder.DropColumn(
                name: "WorkingFolder",
                table: "Councils");
        }
    }
}
