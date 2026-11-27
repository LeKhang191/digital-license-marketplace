using Microsoft.EntityFrameworkCore.Migrations;

namespace LicenseVault.Api.Infrastructure.Persistence.Migrations;

public partial class AddKinguinMockWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Status", table: "kinguin_mock_orders",
            type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "MOCK_FULFILLED");
        migrationBuilder.AddColumn<DateTime>(name: "NextPublishAtUtc", table: "kinguin_mock_orders",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_kinguin_mock_orders_Status_NextPublishAtUtc",
            table: "kinguin_mock_orders", columns: new[] { "Status", "NextPublishAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_kinguin_mock_orders_Status_NextPublishAtUtc", table: "kinguin_mock_orders");
        // Do not turn unfulfilled requests into successful legacy orders on downgrade.
        migrationBuilder.Sql("DELETE FROM kinguin_mock_orders WHERE \"Status\" <> 'MOCK_FULFILLED'");
        migrationBuilder.DropColumn(name: "Status", table: "kinguin_mock_orders");
        migrationBuilder.DropColumn(name: "NextPublishAtUtc", table: "kinguin_mock_orders");
    }
}
