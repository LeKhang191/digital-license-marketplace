using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerApiAndWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "partner_order_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartnerId = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partner_order_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_partner_order_requests_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "partner_webhook_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartnerId = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastStatusCode = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextRetryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partner_webhook_deliveries", x => x.Id);
                    table.CheckConstraint("CK_partner_webhook_deliveries_event", "\"EventType\" IN ('partner.order.completed', 'partner.order.refunded')");
                    table.CheckConstraint("CK_partner_webhook_deliveries_status", "\"Status\" IN ('PENDING', 'DELIVERED', 'FAILED', 'SKIPPED')");
                });

            migrationBuilder.CreateTable(
                name: "partner_webhook_endpoints",
                columns: table => new
                {
                    PartnerId = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    CallbackUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SigningSecret = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partner_webhook_endpoints", x => x.PartnerId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_partner_order_requests_OrderId",
                table: "partner_order_requests",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_partner_order_requests_PartnerId_IdempotencyKey",
                table: "partner_order_requests",
                columns: new[] { "PartnerId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_partner_webhook_deliveries_OrderId_EventType",
                table: "partner_webhook_deliveries",
                columns: new[] { "OrderId", "EventType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_partner_webhook_deliveries_PartnerId_CreatedAtUtc",
                table: "partner_webhook_deliveries",
                columns: new[] { "PartnerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_partner_webhook_deliveries_Status_NextRetryAtUtc",
                table: "partner_webhook_deliveries",
                columns: new[] { "Status", "NextRetryAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partner_order_requests");

            migrationBuilder.DropTable(
                name: "partner_webhook_deliveries");

            migrationBuilder.DropTable(
                name: "partner_webhook_endpoints");
        }
    }
}
