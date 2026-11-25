using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductUnitFactorBelowOne : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductUnits_Values",
                table: "ProductUnits");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductUnits_Values",
                table: "ProductUnits",
                sql: "[Factor] > 0 AND [Factor] <> 1 AND [RetailPrice] >= 0 AND [WholesalePrice] >= 0 AND [MinSalePrice] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductUnits_Values",
                table: "ProductUnits");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductUnits_Values",
                table: "ProductUnits",
                sql: "[Factor] > 1 AND [RetailPrice] >= 0 AND [WholesalePrice] >= 0 AND [MinSalePrice] >= 0");
        }
    }
}
