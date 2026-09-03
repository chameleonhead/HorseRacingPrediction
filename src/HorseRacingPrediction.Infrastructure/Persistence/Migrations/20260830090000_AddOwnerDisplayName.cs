using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HorseRacingPrediction.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EventStoreDbContext))]
[Migration("20260830090000_AddOwnerDisplayName")]
public partial class AddOwnerDisplayName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<bool>("IsDisplayName", "OwnerAliasMappings", type: "INTEGER", nullable: false, defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn("IsDisplayName", "OwnerAliasMappings");
}
