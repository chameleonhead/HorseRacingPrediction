using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HorseRacingPrediction.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EventStoreDbContext))]
[Migration("20260911190000_AddRaceOddsSnapshots")]
public sealed class AddRaceOddsSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "OddsSnapshots", table: "RacePredictionContexts", type: "TEXT", nullable: false,
        defaultValue: "[]");
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "OddsSnapshots", table: "RacePredictionContexts");
}
