using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Cashu.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueSecretConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Proofs_Secret",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs",
                column: "Secret",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Proofs_Secret",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs"
            );
        }
    }
}
