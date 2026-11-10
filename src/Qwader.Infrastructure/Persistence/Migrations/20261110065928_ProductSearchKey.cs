using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductSearchKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchKey",
                table: "Products",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                computedColumnSql: "REPLACE([NormalizedName], N' ', N'')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_SearchKey",
                table: "Products",
                column: "SearchKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_SearchKey",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SearchKey",
                table: "Products");
        }
    }
}
