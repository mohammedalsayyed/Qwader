using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SaleRestoreAndReturnVoid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "SaleReturns",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                table: "SaleReturns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedById",
                table: "SaleReturns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RestoredAt",
                table: "SaleInvoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RestoredById",
                table: "SaleInvoices",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "SaleReturns");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "SaleReturns");

            migrationBuilder.DropColumn(
                name: "VoidedById",
                table: "SaleReturns");

            migrationBuilder.DropColumn(
                name: "RestoredAt",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "RestoredById",
                table: "SaleInvoices");
        }
    }
}
