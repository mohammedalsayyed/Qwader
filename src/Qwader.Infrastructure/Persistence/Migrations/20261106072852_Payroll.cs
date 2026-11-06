using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PayPeriod = table.Column<int>(type: "int", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.CheckConstraint("CK_Employees_Rate", "[Rate] >= 0");
                    table.ForeignKey(
                        name: "FK_Employees_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollSettlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    PayPeriod = table.Column<int>(type: "int", nullable: false),
                    Units = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Gross = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Bonuses = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Deductions = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Advances = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Net = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CarriedAdvance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FinancialAccountId = table.Column<int>(type: "int", nullable: true),
                    ExpenseId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollSettlements_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollSettlements_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollSettlements_FinancialAccounts_FinancialAccountId",
                        column: x => x.FinancialAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ExpenseId = table.Column<int>(type: "int", nullable: true),
                    CarriedFromSettlementId = table.Column<int>(type: "int", nullable: true),
                    SettlementId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollEntries", x => x.Id);
                    table.CheckConstraint("CK_PayrollEntries_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_PayrollEntries_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollEntries_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollEntries_PayrollSettlements_CarriedFromSettlementId",
                        column: x => x.CarriedFromSettlementId,
                        principalTable: "PayrollSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollEntries_PayrollSettlements_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "PayrollSettlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_UserId",
                table: "Employees",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEntries_CarriedFromSettlementId",
                table: "PayrollEntries",
                column: "CarriedFromSettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEntries_EmployeeId_SettlementId_Status",
                table: "PayrollEntries",
                columns: new[] { "EmployeeId", "SettlementId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEntries_ExpenseId",
                table: "PayrollEntries",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEntries_SettlementId",
                table: "PayrollEntries",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSettlements_EmployeeId_PeriodFrom",
                table: "PayrollSettlements",
                columns: new[] { "EmployeeId", "PeriodFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSettlements_ExpenseId",
                table: "PayrollSettlements",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSettlements_FinancialAccountId",
                table: "PayrollSettlements",
                column: "FinancialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSettlements_Number",
                table: "PayrollSettlements",
                column: "Number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollEntries");

            migrationBuilder.DropTable(
                name: "PayrollSettlements");

            migrationBuilder.DropTable(
                name: "Employees");
        }
    }
}
