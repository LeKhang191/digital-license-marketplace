using Microsoft.EntityFrameworkCore.Migrations;

namespace LicenseVault.Api.Infrastructure.Persistence.Migrations;

public partial class AddSupplierApiConfiguration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("supplier_api_configurations", t => new {
            Provider = t.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            EncryptedApiKey = t.Column<string>(type: "text", nullable: false),
            UpdatedAtUtc = t.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_supplier_api_configurations", x => x.Provider));
        m.CreateTable("supplier_product_mappings", t => new {
            Provider = t.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            Mode = t.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
            ProductId = t.Column<Guid>(type: "uuid", nullable: false),
            SupplierProductId = t.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
        }, constraints: t => t.PrimaryKey("PK_supplier_product_mappings", x => new { x.Provider, x.Mode, x.ProductId }));
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("supplier_product_mappings");
        m.DropTable("supplier_api_configurations");
    }
}
