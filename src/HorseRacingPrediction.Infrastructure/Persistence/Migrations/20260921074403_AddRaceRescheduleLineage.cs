using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorseRacingPrediction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRaceRescheduleLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReplacementRaceId",
                table: "RaceSummaries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplacementRaceId",
                table: "RaceResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplacementRaceId",
                table: "RacePredictionContexts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReplacementRaceId",
                table: "RaceSummaries");

            migrationBuilder.DropColumn(
                name: "ReplacementRaceId",
                table: "RaceResults");

            migrationBuilder.DropColumn(
                name: "ReplacementRaceId",
                table: "RacePredictionContexts");
        }
    }
}
