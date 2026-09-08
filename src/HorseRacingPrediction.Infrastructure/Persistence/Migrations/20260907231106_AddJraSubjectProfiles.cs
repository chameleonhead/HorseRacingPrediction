using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorseRacingPrediction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJraSubjectProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JraSubjectProfileReadModel",
                columns: table => new
                {
                    SubjectId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SourceIdentity = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Fields = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JraSubjectProfileReadModel", x => x.SubjectId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JraSubjectProfileReadModel");
        }
    }
}
