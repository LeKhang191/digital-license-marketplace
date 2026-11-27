using Microsoft.EntityFrameworkCore.Migrations;

namespace LicenseVault.Api.Infrastructure.Persistence.Migrations;

public partial class AddKinguinMockOrders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "kinguin_mock_orders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                SupplierProductId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                OwnerId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                TotalCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                EncryptedKeys = table.Column<string>(type: "text", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_kinguin_mock_orders", x => x.Id));
        migrationBuilder.CreateIndex("IX_kinguin_mock_orders_SupplierProductId", "kinguin_mock_orders", "SupplierProductId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("kinguin_mock_orders");
}
