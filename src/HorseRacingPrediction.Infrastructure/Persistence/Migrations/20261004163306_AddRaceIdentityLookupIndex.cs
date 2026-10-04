using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorseRacingPrediction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRaceIdentityLookupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS IX_RacePredictionContexts_RaceDate_RaceNumber
                    ON RacePredictionContexts (RaceDate, RaceNumber);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_RacePredictionContexts_RaceDate_RaceNumber;");
        }
    }
}
