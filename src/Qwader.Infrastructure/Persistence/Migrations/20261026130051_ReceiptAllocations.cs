using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReceiptAllocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptVoucherId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    SaleInvoiceId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptAllocations_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptAllocations_ReceiptVouchers_ReceiptVoucherId",
                        column: x => x.ReceiptVoucherId,
                        principalTable: "ReceiptVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptAllocations_SaleInvoices_SaleInvoiceId",
                        column: x => x.SaleInvoiceId,
                        principalTable: "SaleInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptAllocations_CustomerId_SaleInvoiceId",
                table: "ReceiptAllocations",
                columns: new[] { "CustomerId", "SaleInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptAllocations_ReceiptVoucherId",
                table: "ReceiptAllocations",
                column: "ReceiptVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptAllocations_SaleInvoiceId",
                table: "ReceiptAllocations",
                column: "SaleInvoiceId");

            // Collections made before this change: the till transfers confirmed for an invoice (Posted = 1, DebtCollection = 1).
            migrationBuilder.Sql("""
                INSERT INTO ReceiptAllocations (ReceiptVoucherId, CustomerId, SaleInvoiceId, Amount)
                SELECT v.Id, v.CustomerId, v.SaleInvoiceId, v.Amount FROM ReceiptVouchers v
                WHERE v.SaleInvoiceId IS NOT NULL AND v.CustomerId IS NOT NULL AND v.Status = 1 AND v.Purpose = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReceiptAllocations");
        }
    }
}
