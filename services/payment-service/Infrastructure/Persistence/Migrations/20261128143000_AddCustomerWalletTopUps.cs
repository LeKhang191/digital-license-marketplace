using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerWalletTopUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wallet_topups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VnpTransactionNo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    VnpPayDate = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallet_topups", x => x.Id);
                    table.CheckConstraint("CK_wallet_topups_amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_wallet_topups_status", "\"Status\" IN ('PENDING', 'SUCCEEDED', 'FAILED')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_wallet_topups_CustomerId_CreatedAtUtc",
                table: "wallet_topups",
                columns: new[] { "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_wallet_topups_PaymentRef",
                table: "wallet_topups",
                column: "PaymentRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallet_topups_VnpTransactionNo",
                table: "wallet_topups",
                column: "VnpTransactionNo",
                unique: true,
                filter: "\"VnpTransactionNo\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallet_topups");
        }
    }
}
