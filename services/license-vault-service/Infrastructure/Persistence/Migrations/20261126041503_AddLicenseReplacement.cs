using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LicenseVault.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenseReplacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "license_replacements",
                columns: table => new
                {
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldLicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    NewLicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    HandledBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_replacements", x => x.TicketId);
                    table.ForeignKey(
                        name: "FK_license_replacements_license_keys_NewLicenseId",
                        column: x => x.NewLicenseId,
                        principalTable: "license_keys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_license_replacements_license_keys_OldLicenseId",
                        column: x => x.OldLicenseId,
                        principalTable: "license_keys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_license_replacements_NewLicenseId",
                table: "license_replacements",
                column: "NewLicenseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_replacements_OldLicenseId",
                table: "license_replacements",
                column: "OldLicenseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_replacements_OrderId",
                table: "license_replacements",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "license_replacements");
        }
    }
}
