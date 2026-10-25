using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsWalkIn = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    SourceNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerLedgerEntries", x => x.Id);
                    table.CheckConstraint("CK_CustomerLedger_OneSided", "[Debit] >= 0 AND [Credit] >= 0");
                    table.ForeignKey(
                        name: "FK_CustomerLedgerEntries_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    PriceTier = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GrossTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineDiscountTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InvoiceDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    SalespersonId = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedById = table.Column<int>(type: "int", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleInvoices", x => x.Id);
                    table.CheckConstraint("CK_SaleInvoices_Amounts", "[GrossTotal] >= 0 AND [LineDiscountTotal] >= 0 AND [InvoiceDiscount] >= 0 AND [Total] >= 0 AND [PaidAmount] >= 0 AND [PaidAmount] <= [Total]");
                    table.ForeignKey(
                        name: "FK_SaleInvoices_AspNetUsers_SalespersonId",
                        column: x => x.SalespersonId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleInvoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleInvoices_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SaleInvoiceId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReturnedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PriceOverridden = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleInvoiceLines", x => x.Id);
                    table.CheckConstraint("CK_SaleLines_Quantities", "[Quantity] > 0 AND [ReturnedQuantity] >= 0 AND [ReturnedQuantity] <= [Quantity]");
                    table.ForeignKey(
                        name: "FK_SaleInvoiceLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleInvoiceLines_SaleInvoices_SaleInvoiceId",
                        column: x => x.SaleInvoiceId,
                        principalTable: "SaleInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalePayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SaleInvoiceId = table.Column<int>(type: "int", nullable: false),
                    FinancialAccountId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalePayments", x => x.Id);
                    table.CheckConstraint("CK_SalePayments_Positive", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_SalePayments_FinancialAccounts_FinancialAccountId",
                        column: x => x.FinancialAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalePayments_SaleInvoices_SaleInvoiceId",
                        column: x => x.SaleInvoiceId,
                        principalTable: "SaleInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleReturns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SaleInvoiceId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Settlement = table.Column<int>(type: "int", nullable: false),
                    RefundAccountId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleReturns_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleReturns_FinancialAccounts_RefundAccountId",
                        column: x => x.RefundAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleReturns_SaleInvoices_SaleInvoiceId",
                        column: x => x.SaleInvoiceId,
                        principalTable: "SaleInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SaleReturnLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SaleReturnId = table.Column<int>(type: "int", nullable: false),
                    SaleInvoiceLineId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Condition = table.Column<int>(type: "int", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleReturnLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleReturnLines_SaleInvoiceLines_SaleInvoiceLineId",
                        column: x => x.SaleInvoiceLineId,
                        principalTable: "SaleInvoiceLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleReturnLines_SaleReturns_SaleReturnId",
                        column: x => x.SaleReturnId,
                        principalTable: "SaleReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerLedgerEntries_CustomerId_Date",
                table: "CustomerLedgerEntries",
                columns: new[] { "CustomerId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Code",
                table: "Customers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_IsWalkIn",
                table: "Customers",
                column: "IsWalkIn",
                unique: true,
                filter: "[IsWalkIn] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_NormalizedName",
                table: "Customers",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Phone",
                table: "Customers",
                column: "Phone",
                unique: true,
                filter: "[Phone] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoiceLines_ProductId",
                table: "SaleInvoiceLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoiceLines_SaleInvoiceId",
                table: "SaleInvoiceLines",
                column: "SaleInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_CustomerId_Date",
                table: "SaleInvoices",
                columns: new[] { "CustomerId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_Date",
                table: "SaleInvoices",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_Number",
                table: "SaleInvoices",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_SalespersonId_Date",
                table: "SaleInvoices",
                columns: new[] { "SalespersonId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_Status_Date",
                table: "SaleInvoices",
                columns: new[] { "Status", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_WarehouseId",
                table: "SaleInvoices",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalePayments_FinancialAccountId",
                table: "SalePayments",
                column: "FinancialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SalePayments_SaleInvoiceId",
                table: "SalePayments",
                column: "SaleInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturnLines_SaleInvoiceLineId",
                table: "SaleReturnLines",
                column: "SaleInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturnLines_SaleReturnId",
                table: "SaleReturnLines",
                column: "SaleReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturns_CustomerId",
                table: "SaleReturns",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturns_Date",
                table: "SaleReturns",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturns_Number",
                table: "SaleReturns",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturns_RefundAccountId",
                table: "SaleReturns",
                column: "RefundAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturns_SaleInvoiceId",
                table: "SaleReturns",
                column: "SaleInvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerLedgerEntries");

            migrationBuilder.DropTable(
                name: "SalePayments");

            migrationBuilder.DropTable(
                name: "SaleReturnLines");

            migrationBuilder.DropTable(
                name: "SaleInvoiceLines");

            migrationBuilder.DropTable(
                name: "SaleReturns");

            migrationBuilder.DropTable(
                name: "SaleInvoices");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
