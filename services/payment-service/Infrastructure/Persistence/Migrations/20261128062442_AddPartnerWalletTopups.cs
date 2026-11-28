using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerWalletTopups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_wallet_transactions_type",
                table: "wallet_transactions");

            migrationBuilder.CreateTable(
                name: "partner_wallet_topups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartnerId = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WalletTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partner_wallet_topups", x => x.Id);
                    table.CheckConstraint("CK_partner_wallet_topups_amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_partner_wallet_topups_balance", "\"BalanceAfter\" >= 0");
                    table.ForeignKey(
                        name: "FK_partner_wallet_topups_wallet_transactions_WalletTransaction~",
                        column: x => x.WalletTransactionId,
                        principalTable: "wallet_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_wallet_transactions_type",
                table: "wallet_transactions",
                sql: "\"Type\" IN ('REFUND', 'PURCHASE', 'TOPUP')");

            migrationBuilder.CreateIndex(
                name: "IX_partner_wallet_topups_PartnerId_CreatedAtUtc",
                table: "partner_wallet_topups",
                columns: new[] { "PartnerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_partner_wallet_topups_PartnerId_IdempotencyKey",
                table: "partner_wallet_topups",
                columns: new[] { "PartnerId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_partner_wallet_topups_WalletTransactionId",
                table: "partner_wallet_topups",
                column: "WalletTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partner_wallet_topups");

            migrationBuilder.DropCheckConstraint(
                name: "CK_wallet_transactions_type",
                table: "wallet_transactions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_wallet_transactions_type",
                table: "wallet_transactions",
                sql: "\"Type\" IN ('REFUND', 'PURCHASE')");
        }
    }
}
