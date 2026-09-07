using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorseRacingPrediction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRaceReacquisitionMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CornerPassagesText",
                table: "RacePredictionContexts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CourseLayout",
                table: "RacePredictionContexts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverallPaceText",
                table: "RacePredictionContexts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "RacePredictionContexts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CornerPassagesText",
                table: "RacePredictionContexts");

            migrationBuilder.DropColumn(
                name: "CourseLayout",
                table: "RacePredictionContexts");

            migrationBuilder.DropColumn(
                name: "OverallPaceText",
                table: "RacePredictionContexts");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "RacePredictionContexts");
        }
    }
}
