using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LicenseVault.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenseRevealTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RevealedAtUtc",
                table: "license_keys",
                type: "timestamp with time zone",
                nullable: true);
        
            // Historical disclosure backfill.
            migrationBuilder.Sql("""
                UPDATE license_keys
                SET "RevealedAtUtc" =
                    COALESCE("AssignedAtUtc", "CreatedAtUtc")
                WHERE "RevealedAtUtc" IS NULL
                  AND (
                      "Status" = 'ASSIGNED'
                      OR "AssignedAtUtc" IS NOT NULL
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RevealedAtUtc",
                table: "license_keys");
        }
    }
}
