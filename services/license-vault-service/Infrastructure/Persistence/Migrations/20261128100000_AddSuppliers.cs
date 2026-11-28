using Microsoft.EntityFrameworkCore.Migrations;

namespace LicenseVault.Api.Infrastructure.Persistence.Migrations;

public partial class AddSuppliers : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("suppliers", t => new {
            Id = t.Column<Guid>(type: "uuid", nullable: false),
            Name = t.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
            NormalizedName = t.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
            Website = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            Email = t.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
            Phone = t.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
            Notes = t.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
            IsActive = t.Column<bool>(type: "boolean", nullable: false),
            CreatedAtUtc = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAtUtc = t.Column<DateTime>(type: "timestamp with time zone", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_suppliers", x => x.Id));
        m.CreateIndex("IX_suppliers_NormalizedName", "suppliers", "NormalizedName", unique: true);
        m.AddColumn<Guid>("SupplierId", "license_import_batches", type: "uuid", nullable: true);
        m.Sql("""
            INSERT INTO suppliers ("Id", "Name", "NormalizedName", "IsActive", "CreatedAtUtc", "UpdatedAtUtc")
            SELECT md5(lower(btrim("SupplierName")))::uuid, min(btrim("SupplierName")), lower(btrim("SupplierName")), true, min("CreatedAtUtc"), now()
            FROM license_import_batches
            WHERE btrim("SupplierName") <> '' AND "SupplierName" <> 'Unspecified (legacy import)'
            GROUP BY lower(btrim("SupplierName"));
            UPDATE license_import_batches b SET "SupplierId" = s."Id"
            FROM suppliers s WHERE lower(btrim(b."SupplierName")) = s."NormalizedName";
            """);
        m.CreateIndex("IX_license_import_batches_SupplierId", "license_import_batches", "SupplierId");
        m.AddForeignKey("FK_license_import_batches_suppliers_SupplierId", "license_import_batches", "SupplierId", "suppliers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropForeignKey("FK_license_import_batches_suppliers_SupplierId", "license_import_batches");
        m.DropIndex("IX_license_import_batches_SupplierId", "license_import_batches");
        m.DropColumn("SupplierId", "license_import_batches");
        m.DropTable("suppliers");
    }
}
