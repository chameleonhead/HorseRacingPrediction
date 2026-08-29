using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorseRacingPrediction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerAliasAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OwnerAliasMappings",
                columns: table => new
                {
                    NormalizedAlias = table.Column<string>(type: "TEXT", nullable: false),
                    AliasName = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerAliasMappings", x => x.NormalizedAlias);
                });

            migrationBuilder.CreateTable(
                name: "OwnerMergeAudits",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceOwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetOwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceNames = table.Column<string>(type: "TEXT", nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerMergeAudits", x => x.AuditId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerAliasMappings_OwnerId",
                table: "OwnerAliasMappings",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerMergeAudits_TargetOwnerId_CreatedAt",
                table: "OwnerMergeAudits",
                columns: new[] { "TargetOwnerId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnerAliasMappings");

            migrationBuilder.DropTable(
                name: "OwnerMergeAudits");
        }
    }
}
