using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.Infrastructure.Persistence.Migrations
{
    public partial class RemoveLegacyCustomerSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_messages");

            migrationBuilder.DropTable(
                name: "support_tickets");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException(
                "Restore Support data from the pre-cutover backup.");
        }
    }
}
