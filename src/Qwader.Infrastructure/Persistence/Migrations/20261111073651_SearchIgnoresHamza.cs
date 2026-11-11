using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qwader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SearchIgnoresHamza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Search text no longer keeps a standalone hamza («ماء» = «ما»): stored names follow, so old and new search the same way.
            // Only the search columns change; the names people see stay as typed. Products' SearchKey is computed from NormalizedName.
            foreach (var table in new[] { "Products", "Categories", "Customers", "Suppliers" })
                migrationBuilder.Sql($"UPDATE [{table}] SET [NormalizedName] = REPLACE([NormalizedName], N'ء', N'') WHERE [NormalizedName] LIKE N'%ء%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
