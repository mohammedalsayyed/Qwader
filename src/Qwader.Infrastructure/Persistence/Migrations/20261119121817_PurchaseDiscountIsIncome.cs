using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseDiscountIsIncome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DiscountIsIncome",
                table: "PurchaseInvoices",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountIsIncome",
                table: "PurchaseInvoices");
        }
    }
}
