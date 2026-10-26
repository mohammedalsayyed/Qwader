using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceCollectedAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CollectedAmount",
                table: "SaleInvoices",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Till transfers already confirmed before this change: count them on their invoices (Posted = 1, DebtCollection = 1).
            migrationBuilder.Sql("""
                UPDATE s SET CollectedAmount = v.Amount
                FROM SaleInvoices s
                JOIN (SELECT SaleInvoiceId, SUM(Amount) AS Amount FROM ReceiptVouchers
                      WHERE SaleInvoiceId IS NOT NULL AND Status = 1 AND Purpose = 1 GROUP BY SaleInvoiceId) v ON v.SaleInvoiceId = s.Id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CollectedAmount",
                table: "SaleInvoices");
        }
    }
}
