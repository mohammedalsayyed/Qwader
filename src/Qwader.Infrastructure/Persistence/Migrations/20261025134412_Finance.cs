using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Finance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FromAccountId = table.Column<int>(type: "int", nullable: false),
                    ToAccountId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountTransfers", x => x.Id);
                    table.CheckConstraint("CK_AccountTransfers_Valid", "[Amount] > 0 AND [FromAccountId] <> [ToAccountId]");
                    table.ForeignKey(
                        name: "FK_AccountTransfers_FinancialAccounts_FromAccountId",
                        column: x => x.FromAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountTransfers_FinancialAccounts_ToAccountId",
                        column: x => x.ToAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IncomingTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SenderName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedSenderName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SenderPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FinancialAccountId = table.Column<int>(type: "int", nullable: false),
                    ReferenceNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TransferDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProofFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    SaleInvoiceId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReviewedById = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ReceiptVoucherId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingTransfers", x => x.Id);
                    table.CheckConstraint("CK_IncomingTransfers_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_IncomingTransfers_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncomingTransfers_FinancialAccounts_FinancialAccountId",
                        column: x => x.FinancialAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncomingTransfers_SaleInvoices_SaleInvoiceId",
                        column: x => x.SaleInvoiceId,
                        principalTable: "SaleInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BeneficiaryName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FinancialAccountId = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AttachmentFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_PaymentVouchers", x => x.Id);
                    table.CheckConstraint("CK_PaymentVouchers_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_PaymentVouchers_FinancialAccounts_FinancialAccountId",
                        column: x => x.FinancialAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentVouchers_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PayerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PayerPhone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    SaleInvoiceId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FinancialAccountId = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AttachmentFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IncomingTransferId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_ReceiptVouchers", x => x.Id);
                    table.CheckConstraint("CK_ReceiptVouchers_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_ReceiptVouchers_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptVouchers_FinancialAccounts_FinancialAccountId",
                        column: x => x.FinancialAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptVouchers_SaleInvoices_SaleInvoiceId",
                        column: x => x.SaleInvoiceId,
                        principalTable: "SaleInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpenseCategoryId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FinancialAccountId = table.Column<int>(type: "int", nullable: false),
                    AttachmentFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentVoucherId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedById = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.CheckConstraint("CK_Expenses_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_Expenses_ExpenseCategories_ExpenseCategoryId",
                        column: x => x.ExpenseCategoryId,
                        principalTable: "ExpenseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_FinancialAccounts_FinancialAccountId",
                        column: x => x.FinancialAccountId,
                        principalTable: "FinancialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_PaymentVouchers_PaymentVoucherId",
                        column: x => x.PaymentVoucherId,
                        principalTable: "PaymentVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransfers_FromAccountId",
                table: "AccountTransfers",
                column: "FromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransfers_Number",
                table: "AccountTransfers",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransfers_ToAccountId",
                table: "AccountTransfers",
                column: "ToAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_Date_ExpenseCategoryId",
                table: "Expenses",
                columns: new[] { "Date", "ExpenseCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExpenseCategoryId",
                table: "Expenses",
                column: "ExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_FinancialAccountId",
                table: "Expenses",
                column: "FinancialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_Number",
                table: "Expenses",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_PaymentVoucherId",
                table: "Expenses",
                column: "PaymentVoucherId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingTransfers_CustomerId",
                table: "IncomingTransfers",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingTransfers_NormalizedSenderName",
                table: "IncomingTransfers",
                column: "NormalizedSenderName");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingTransfers_Number",
                table: "IncomingTransfers",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingTransfers_SaleInvoiceId",
                table: "IncomingTransfers",
                column: "SaleInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingTransfers_Status_TransferDate",
                table: "IncomingTransfers",
                columns: new[] { "Status", "TransferDate" });

            migrationBuilder.CreateIndex(
                name: "UX_IncomingTransfers_Reference",
                table: "IncomingTransfers",
                columns: new[] { "FinancialAccountId", "ReferenceNo" },
                unique: true,
                filter: "[ReferenceNo] IS NOT NULL AND [Status] <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentVouchers_Date",
                table: "PaymentVouchers",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentVouchers_FinancialAccountId",
                table: "PaymentVouchers",
                column: "FinancialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentVouchers_Number",
                table: "PaymentVouchers",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentVouchers_SupplierId_Date",
                table: "PaymentVouchers",
                columns: new[] { "SupplierId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_CustomerId_Date",
                table: "ReceiptVouchers",
                columns: new[] { "CustomerId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_Date",
                table: "ReceiptVouchers",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_FinancialAccountId",
                table: "ReceiptVouchers",
                column: "FinancialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_Number",
                table: "ReceiptVouchers",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVouchers_SaleInvoiceId",
                table: "ReceiptVouchers",
                column: "SaleInvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountTransfers");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "IncomingTransfers");

            migrationBuilder.DropTable(
                name: "ReceiptVouchers");

            migrationBuilder.DropTable(
                name: "PaymentVouchers");
        }
    }
}
