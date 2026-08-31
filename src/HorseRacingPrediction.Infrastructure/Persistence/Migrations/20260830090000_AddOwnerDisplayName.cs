using Microsoft.EntityFrameworkCore.Migrations;

namespace HorseRacingPrediction.Infrastructure.Persistence.Migrations;

public partial class AddOwnerDisplayName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<bool>("IsDisplayName", "OwnerAliasMappings", type: "INTEGER", nullable: false, defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn("IsDisplayName", "OwnerAliasMappings");
}
