using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "support_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AssignedRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    HandledBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_support_tickets_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "support_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SenderRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Content = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_support_messages_support_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "support_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_support_messages_TicketId_CreatedAtUtc",
                table: "support_messages",
                columns: new[] { "TicketId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_AssignedRole_Status",
                table: "support_tickets",
                columns: new[] { "AssignedRole", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_CustomerId",
                table: "support_tickets",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_OrderId_Category",
                table: "support_tickets",
                columns: new[] { "OrderId", "Category" },
                unique: true,
                filter: "\"Status\" IN ('OPEN', 'IN_PROGRESS')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_messages");

            migrationBuilder.DropTable(
                name: "support_tickets");
        }
    }
}
