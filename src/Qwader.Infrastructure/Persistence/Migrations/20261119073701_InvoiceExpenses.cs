using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchaseInvoiceId",
                table: "Expenses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SaleInvoiceId",
                table: "Expenses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_PurchaseInvoiceId",
                table: "Expenses",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_SaleInvoiceId",
                table: "Expenses",
                column: "SaleInvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_PurchaseInvoices_PurchaseInvoiceId",
                table: "Expenses",
                column: "PurchaseInvoiceId",
                principalTable: "PurchaseInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_SaleInvoices_SaleInvoiceId",
                table: "Expenses",
                column: "SaleInvoiceId",
                principalTable: "SaleInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_PurchaseInvoices_PurchaseInvoiceId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_SaleInvoices_SaleInvoiceId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_PurchaseInvoiceId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_SaleInvoiceId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "PurchaseInvoiceId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "SaleInvoiceId",
                table: "Expenses");
        }
    }
}
