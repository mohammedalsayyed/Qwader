using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdvanceIsEmployeeDebt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceExpense",
                table: "PayrollSettlements",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PaymentVoucherId",
                table: "PayrollEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEntries_PaymentVoucherId",
                table: "PayrollEntries",
                column: "PaymentVoucherId");

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollEntries_PaymentVouchers_PaymentVoucherId",
                table: "PayrollEntries",
                column: "PaymentVoucherId",
                principalTable: "PaymentVouchers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollEntries_PaymentVouchers_PaymentVoucherId",
                table: "PayrollEntries");

            migrationBuilder.DropIndex(
                name: "IX_PayrollEntries_PaymentVoucherId",
                table: "PayrollEntries");

            migrationBuilder.DropColumn(
                name: "AdvanceExpense",
                table: "PayrollSettlements");

            migrationBuilder.DropColumn(
                name: "PaymentVoucherId",
                table: "PayrollEntries");
        }
    }
}
