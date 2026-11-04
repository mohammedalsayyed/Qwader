using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfflineSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockBalances_NonNegative",
                table: "StockBalances");

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "SaleInvoices",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "SaleInvoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewedById",
                table: "SaleInvoices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SoldOffline",
                table: "SaleInvoices",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "ReviewedById",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "SoldOffline",
                table: "SaleInvoices");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockBalances_NonNegative",
                table: "StockBalances",
                sql: "[Quantity] >= 0");
        }
    }
}
