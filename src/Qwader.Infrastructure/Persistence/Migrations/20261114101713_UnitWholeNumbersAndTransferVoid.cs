using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnitWholeNumbersAndTransferVoid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WholeNumbersOnly",
                table: "Units",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "AccountTransfers",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                table: "AccountTransfers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedById",
                table: "AccountTransfers",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WholeNumbersOnly",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "AccountTransfers");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "AccountTransfers");

            migrationBuilder.DropColumn(
                name: "VoidedById",
                table: "AccountTransfers");
        }
    }
}
