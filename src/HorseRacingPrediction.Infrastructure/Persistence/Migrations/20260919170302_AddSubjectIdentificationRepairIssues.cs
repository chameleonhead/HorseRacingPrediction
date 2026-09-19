using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorseRacingPrediction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubjectIdentificationRepairIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubjectIdentificationRepairIssues",
                columns: table => new
                {
                    IssueId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectType = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectName = table.Column<string>(type: "TEXT", nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", nullable: false),
                    RequestedByRaceId = table.Column<string>(type: "TEXT", nullable: true),
                    SourceIdentity = table.Column<string>(type: "TEXT", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ReasonCode = table.Column<string>(type: "TEXT", nullable: false),
                    ReasonMessage = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    Occurrence = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TargetSubjectId = table.Column<string>(type: "TEXT", nullable: true),
                    RecoveryTaskId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectIdentificationRepairIssues", x => x.IssueId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubjectIdentificationRepairIssues_EvidenceFingerprint",
                table: "SubjectIdentificationRepairIssues",
                column: "EvidenceFingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubjectIdentificationRepairIssues_Status_CreatedAt",
                table: "SubjectIdentificationRepairIssues",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubjectIdentificationRepairIssues");
        }
    }
}
