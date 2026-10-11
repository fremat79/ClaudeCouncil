using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaudeCouncilManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Councils",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    InitialPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    ExtraInstructions = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Councils", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Experts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Folder = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Effort = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    IsCoordinator = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Experts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CouncilMembers",
                columns: table => new
                {
                    CouncilId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpertId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouncilMembers", x => new { x.CouncilId, x.ExpertId });
                    table.ForeignKey(
                        name: "FK_CouncilMembers_Councils_CouncilId",
                        column: x => x.CouncilId,
                        principalTable: "Councils",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CouncilMembers_Experts_ExpertId",
                        column: x => x.ExpertId,
                        principalTable: "Experts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CouncilMembers_ExpertId",
                table: "CouncilMembers",
                column: "ExpertId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CouncilMembers");

            migrationBuilder.DropTable(
                name: "Councils");

            migrationBuilder.DropTable(
                name: "Experts");
        }
    }
}
