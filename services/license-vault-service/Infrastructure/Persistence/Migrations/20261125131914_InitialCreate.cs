using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LicenseVault.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "license_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedKey = table.Column<string>(type: "text", nullable: false),
                    KeyFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_keys", x => x.Id);
                    table.CheckConstraint("CK_license_keys_Status", "\"Status\" IN ('AVAILABLE', 'ASSIGNED', 'DISABLED')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_license_keys_CustomerId",
                table: "license_keys",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_license_keys_KeyFingerprint",
                table: "license_keys",
                column: "KeyFingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_keys_OrderId",
                table: "license_keys",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_license_keys_OrderItemId",
                table: "license_keys",
                column: "OrderItemId",
                unique: true,
                filter: "\"OrderItemId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_license_keys_ProductId_Status",
                table: "license_keys",
                columns: new[] { "ProductId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "license_keys");
        }
    }
}
