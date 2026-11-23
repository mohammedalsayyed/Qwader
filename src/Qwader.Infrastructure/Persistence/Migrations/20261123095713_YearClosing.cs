using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class YearClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClosedYears",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedById = table.Column<int>(type: "int", nullable: true),
                    NetProfit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAssets = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalLiabilities = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalEquity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Difference = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Snapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReopenedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReopenedById = table.Column<int>(type: "int", nullable: true),
                    ReopenReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClosedYears", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClosedYears_Year",
                table: "ClosedYears",
                column: "Year");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClosedYears");
        }
    }
}
