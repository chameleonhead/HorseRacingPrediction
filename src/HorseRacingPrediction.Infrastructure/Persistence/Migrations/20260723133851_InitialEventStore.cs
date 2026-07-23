using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorseRacingPrediction.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialEventStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventEntity",
                columns: table => new
                {
                    GlobalSequenceNumber = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AggregateId = table.Column<string>(type: "TEXT", nullable: false),
                    AggregateName = table.Column<string>(type: "TEXT", nullable: false),
                    AggregateSequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false),
                    Metadata = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventEntity", x => x.GlobalSequenceNumber);
                });

            migrationBuilder.CreateTable(
                name: "HorseRaceHistories",
                columns: table => new
                {
                    HorseId = table.Column<string>(type: "TEXT", nullable: false),
                    Entries = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HorseRaceHistories", x => x.HorseId);
                });

            migrationBuilder.CreateTable(
                name: "Horses",
                columns: table => new
                {
                    HorseId = table.Column<string>(type: "TEXT", nullable: false),
                    RegisteredName = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    SexCode = table.Column<string>(type: "TEXT", nullable: true),
                    BirthDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    OwnerName = table.Column<string>(type: "TEXT", nullable: true),
                    Aliases = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Horses", x => x.HorseId);
                });

            migrationBuilder.CreateTable(
                name: "HorseWeightHistories",
                columns: table => new
                {
                    HorseId = table.Column<string>(type: "TEXT", nullable: false),
                    WeightHistory = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HorseWeightHistories", x => x.HorseId);
                });

            migrationBuilder.CreateTable(
                name: "JockeyRaceHistories",
                columns: table => new
                {
                    JockeyId = table.Column<string>(type: "TEXT", nullable: false),
                    Entries = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JockeyRaceHistories", x => x.JockeyId);
                });

            migrationBuilder.CreateTable(
                name: "Jockeys",
                columns: table => new
                {
                    JockeyId = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    AffiliationCode = table.Column<string>(type: "TEXT", nullable: true),
                    Aliases = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jockeys", x => x.JockeyId);
                });

            migrationBuilder.CreateTable(
                name: "MemoSubjects",
                columns: table => new
                {
                    SubjectKey = table.Column<string>(type: "TEXT", nullable: false),
                    Memos = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoSubjects", x => x.SubjectKey);
                });

            migrationBuilder.CreateTable(
                name: "PredictionComparisons",
                columns: table => new
                {
                    RaceId = table.Column<string>(type: "TEXT", nullable: false),
                    RaceName = table.Column<string>(type: "TEXT", nullable: true),
                    WinningHorseName = table.Column<string>(type: "TEXT", nullable: true),
                    ResultDeclaredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TicketStates = table.Column<string>(type: "TEXT", nullable: false),
                    EntryIndexes = table.Column<string>(type: "TEXT", nullable: false),
                    EntryResults = table.Column<string>(type: "TEXT", nullable: false),
                    PayoutResult = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictionComparisons", x => x.RaceId);
                });

            migrationBuilder.CreateTable(
                name: "PredictionTickets",
                columns: table => new
                {
                    PredictionTicketId = table.Column<string>(type: "TEXT", nullable: false),
                    RaceId = table.Column<string>(type: "TEXT", nullable: true),
                    PredictorType = table.Column<string>(type: "TEXT", nullable: true),
                    PredictorId = table.Column<string>(type: "TEXT", nullable: true),
                    ConfidenceScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    SummaryComment = table.Column<string>(type: "TEXT", nullable: true),
                    PredictedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    TicketStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    Marks = table.Column<string>(type: "TEXT", nullable: false),
                    Evaluations = table.Column<string>(type: "TEXT", nullable: false),
                    EvaluationStatus = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictionTickets", x => x.PredictionTicketId);
                });

            migrationBuilder.CreateTable(
                name: "RacePredictionContexts",
                columns: table => new
                {
                    RaceId = table.Column<string>(type: "TEXT", nullable: false),
                    RaceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RacecourseCode = table.Column<string>(type: "TEXT", nullable: true),
                    RaceNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    RaceName = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: true),
                    SurfaceCode = table.Column<string>(type: "TEXT", nullable: true),
                    DistanceMeters = table.Column<int>(type: "INTEGER", nullable: true),
                    DirectionCode = table.Column<string>(type: "TEXT", nullable: true),
                    Entries = table.Column<string>(type: "TEXT", nullable: false),
                    WeatherObservations = table.Column<string>(type: "TEXT", nullable: false),
                    TrackConditionObservations = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RacePredictionContexts", x => x.RaceId);
                });

            migrationBuilder.CreateTable(
                name: "RaceResults",
                columns: table => new
                {
                    RaceId = table.Column<string>(type: "TEXT", nullable: false),
                    RaceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RacecourseCode = table.Column<string>(type: "TEXT", nullable: true),
                    RaceNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    RaceName = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    EntryCount = table.Column<int>(type: "INTEGER", nullable: true),
                    WinningHorseName = table.Column<string>(type: "TEXT", nullable: true),
                    WinningHorseId = table.Column<string>(type: "TEXT", nullable: true),
                    ResultDeclaredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    StewardReportText = table.Column<string>(type: "TEXT", nullable: true),
                    EntryResults = table.Column<string>(type: "TEXT", nullable: false),
                    EntryIndexes = table.Column<string>(type: "TEXT", nullable: false),
                    PayoutResult = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaceResults", x => x.RaceId);
                });

            migrationBuilder.CreateTable(
                name: "RaceSummaries",
                columns: table => new
                {
                    RaceId = table.Column<string>(type: "TEXT", nullable: false),
                    RaceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RacecourseCode = table.Column<string>(type: "TEXT", nullable: true),
                    RaceNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    RaceName = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    EntryCount = table.Column<int>(type: "INTEGER", nullable: true),
                    WinningHorseName = table.Column<string>(type: "TEXT", nullable: true),
                    ResultDeclaredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaceSummaries", x => x.RaceId);
                });

            migrationBuilder.CreateTable(
                name: "SnapshotEntity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AggregateId = table.Column<string>(type: "TEXT", nullable: false),
                    AggregateName = table.Column<string>(type: "TEXT", nullable: false),
                    AggregateSequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false),
                    Metadata = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnapshotEntity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trainers",
                columns: table => new
                {
                    TrainerId = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    AffiliationCode = table.Column<string>(type: "TEXT", nullable: true),
                    Aliases = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trainers", x => x.TrainerId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventEntity_AggregateId_AggregateSequenceNumber",
                table: "EventEntity",
                columns: new[] { "AggregateId", "AggregateSequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotEntity_AggregateName_AggregateId_AggregateSequenceNumber",
                table: "SnapshotEntity",
                columns: new[] { "AggregateName", "AggregateId", "AggregateSequenceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventEntity");

            migrationBuilder.DropTable(
                name: "HorseRaceHistories");

            migrationBuilder.DropTable(
                name: "Horses");

            migrationBuilder.DropTable(
                name: "HorseWeightHistories");

            migrationBuilder.DropTable(
                name: "JockeyRaceHistories");

            migrationBuilder.DropTable(
                name: "Jockeys");

            migrationBuilder.DropTable(
                name: "MemoSubjects");

            migrationBuilder.DropTable(
                name: "PredictionComparisons");

            migrationBuilder.DropTable(
                name: "PredictionTickets");

            migrationBuilder.DropTable(
                name: "RacePredictionContexts");

            migrationBuilder.DropTable(
                name: "RaceResults");

            migrationBuilder.DropTable(
                name: "RaceSummaries");

            migrationBuilder.DropTable(
                name: "SnapshotEntity");

            migrationBuilder.DropTable(
                name: "Trainers");
        }
    }
}
