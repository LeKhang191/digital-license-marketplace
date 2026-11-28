using Microsoft.EntityFrameworkCore.Migrations;

namespace LicenseVault.Api.Infrastructure.Persistence.Migrations;

public partial class AddLicenseImportBatches : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("license_import_batches", t => new {
            Id = t.Column<Guid>(type: "uuid", nullable: false),
            ProductId = t.Column<Guid>(type: "uuid", nullable: false),
            SupplierName = t.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
            Reference = t.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
            UnitCostVnd = t.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
            Method = t.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
            ImportedBy = t.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
            CreatedAtUtc = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            Total = t.Column<int>(type: "integer", nullable: false),
            Imported = t.Column<int>(type: "integer", nullable: false),
            Duplicates = t.Column<int>(type: "integer", nullable: false),
            Invalid = t.Column<int>(type: "integer", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_license_import_batches", x => x.Id));
        m.CreateIndex("IX_license_import_batches_CreatedAtUtc", "license_import_batches", "CreatedAtUtc");
        m.AddColumn<Guid>("ImportBatchId", "license_keys", type: "uuid", nullable: true);
        m.CreateIndex("IX_license_keys_ImportBatchId", "license_keys", "ImportBatchId");
        m.AddForeignKey("FK_license_keys_license_import_batches_ImportBatchId", "license_keys", "ImportBatchId", "license_import_batches", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropForeignKey("FK_license_keys_license_import_batches_ImportBatchId", "license_keys");
        m.DropIndex("IX_license_keys_ImportBatchId", "license_keys");
        m.DropColumn("ImportBatchId", "license_keys");
        m.DropTable("license_import_batches");
    }
}
