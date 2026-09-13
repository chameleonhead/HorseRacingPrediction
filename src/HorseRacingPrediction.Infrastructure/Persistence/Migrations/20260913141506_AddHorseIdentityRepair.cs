using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorseRacingPrediction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHorseIdentityRepair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HorseIdentityRepairCandidates",
                columns: table => new
                {
                    CandidateId = table.Column<string>(type: "TEXT", nullable: false),
                    RepairId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceHorseId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetHorseId = table.Column<string>(type: "TEXT", nullable: false),
                    JraIdentity = table.Column<string>(type: "TEXT", nullable: false),
                    RaceId = table.Column<string>(type: "TEXT", nullable: false),
                    EntryId = table.Column<string>(type: "TEXT", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HorseIdentityRepairCandidates", x => x.CandidateId);
                });

            migrationBuilder.CreateTable(
                name: "HorseIdentityRepairRedirects",
                columns: table => new
                {
                    SourceHorseId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetHorseId = table.Column<string>(type: "TEXT", nullable: false),
                    RepairId = table.Column<string>(type: "TEXT", nullable: false),
                    JraIdentity = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HorseIdentityRepairRedirects", x => x.SourceHorseId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HorseIdentityRepairCandidates_RepairId_AppliedAt",
                table: "HorseIdentityRepairCandidates",
                columns: new[] { "RepairId", "AppliedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HorseIdentityRepairCandidates_SourceHorseId_TargetHorseId",
                table: "HorseIdentityRepairCandidates",
                columns: new[] { "SourceHorseId", "TargetHorseId" });

            migrationBuilder.CreateIndex(
                name: "IX_HorseIdentityRepairRedirects_TargetHorseId",
                table: "HorseIdentityRepairRedirects",
                column: "TargetHorseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HorseIdentityRepairCandidates");

            migrationBuilder.DropTable(
                name: "HorseIdentityRepairRedirects");
        }
    }
}
