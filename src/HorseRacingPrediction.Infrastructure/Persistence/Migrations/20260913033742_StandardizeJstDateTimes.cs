using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorseRacingPrediction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StandardizeJstDateTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Normalize(migrationBuilder, "JraSubjectProfileReadModel", "AcquiredAt");
            Normalize(migrationBuilder, "OwnerAliasMappings", "CreatedAt");
            Normalize(migrationBuilder, "OwnerMergeAudits", "CreatedAt");
            Normalize(migrationBuilder, "PredictionComparisons", "ResultDeclaredAt");
            Normalize(migrationBuilder, "PredictionTickets", "PredictedAt");
            Normalize(migrationBuilder, "RaceResults", "ResultDeclaredAt");
            Normalize(migrationBuilder, "RaceSummaries", "ResultDeclaredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // タイムゾーンなしJSTから元のoffset表現は復元できないため、データ変換は戻さない。
        }

        private static void Normalize(MigrationBuilder migrationBuilder, string table, string column)
        {
            migrationBuilder.Sql($$"""
                UPDATE "{{table}}"
                SET "{{column}}" = strftime('%Y-%m-%d %H:%M:%f', "{{column}}", '+9 hours')
                WHERE "{{column}}" IS NOT NULL
                  AND (
                    substr("{{column}}", -1, 1) = 'Z'
                    OR instr(substr("{{column}}", 12), '+') > 0
                    OR instr(substr("{{column}}", 12), '-') > 0
                  );
                """);
        }
    }
}
