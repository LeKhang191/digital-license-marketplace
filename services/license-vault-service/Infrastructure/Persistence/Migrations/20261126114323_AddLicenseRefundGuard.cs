using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LicenseVault.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenseRefundGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "license_refund_guards",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_refund_guards", x => x.OrderId);
                    table.CheckConstraint("CK_license_refund_guard_mode", "\"Mode\" IN ('UNREVEALED', 'OUT_OF_STOCK', 'VERIFIED_DEFECT')");
                    table.CheckConstraint("CK_license_refund_guard_status", "\"Status\" IN ('PREPARED', 'FINALIZED')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_license_refund_guards_RequestId",
                table: "license_refund_guards",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "license_refund_guards");
        }
    }
}
