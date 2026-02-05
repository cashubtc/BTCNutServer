using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Cashu.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueKeysetIdConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MintKeys_KeysetId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "MintKeys",
                column: "KeysetId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MintKeys_KeysetId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "MintKeys");
        }
    }
}
