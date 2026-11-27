using Microsoft.EntityFrameworkCore.Migrations;
namespace Catalog.Api.Infrastructure.Persistence.Migrations;
public partial class AddGameMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(name: "SteamMetadataJson", table: "products", type: "text", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "SteamMetadataJson", table: "products");
}
