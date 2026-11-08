using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiUnitProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductUnitId",
                table: "SaleInvoiceLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "SaleInvoiceLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "UnitName",
                table: "SaleInvoiceLines",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductUnitId",
                table: "PurchaseInvoiceLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "PurchaseInvoiceLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "UnitName",
                table: "PurchaseInvoiceLines",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    Factor = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    RetailPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WholesalePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MinSalePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductUnits", x => x.Id);
                    table.CheckConstraint("CK_ProductUnits_Values", "[Factor] > 1 AND [RetailPrice] >= 0 AND [WholesalePrice] >= 0 AND [MinSalePrice] >= 0");
                    table.ForeignKey(
                        name: "FK_ProductUnits_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductUnits_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoiceLines_ProductUnitId",
                table: "SaleInvoiceLines",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_ProductUnitId",
                table: "PurchaseInvoiceLines",
                column: "ProductUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnits_ProductId_UnitId",
                table: "ProductUnits",
                columns: new[] { "ProductId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnits_UnitId",
                table: "ProductUnits",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoiceLines_ProductUnits_ProductUnitId",
                table: "PurchaseInvoiceLines",
                column: "ProductUnitId",
                principalTable: "ProductUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleInvoiceLines_ProductUnits_ProductUnitId",
                table: "SaleInvoiceLines",
                column: "ProductUnitId",
                principalTable: "ProductUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoiceLines_ProductUnits_ProductUnitId",
                table: "PurchaseInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleInvoiceLines_ProductUnits_ProductUnitId",
                table: "SaleInvoiceLines");

            migrationBuilder.DropTable(
                name: "ProductUnits");

            migrationBuilder.DropIndex(
                name: "IX_SaleInvoiceLines_ProductUnitId",
                table: "SaleInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceLines_ProductUnitId",
                table: "PurchaseInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ProductUnitId",
                table: "SaleInvoiceLines");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "SaleInvoiceLines");

            migrationBuilder.DropColumn(
                name: "UnitName",
                table: "SaleInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ProductUnitId",
                table: "PurchaseInvoiceLines");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "PurchaseInvoiceLines");

            migrationBuilder.DropColumn(
                name: "UnitName",
                table: "PurchaseInvoiceLines");
        }
    }
}
