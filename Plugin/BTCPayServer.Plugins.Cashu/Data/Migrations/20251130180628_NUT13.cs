using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Cashu.Data.Migrations
{
    /// <inheritdoc />
    public partial class NUT13 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "P2PkE",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashuWalletConfig",
                schema: "BTCPayServer.Plugins.Cashu",
                columns: table => new
                {
                    StoreId = table.Column<string>(type: "text", nullable: false),
                    WalletMnemonic = table.Column<string>(type: "text", nullable: true),
                    Verified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashuWalletConfig", x => x.StoreId);
                });

            migrationBuilder.CreateTable(
                name: "StoreKeysetCounters",
                schema: "BTCPayServer.Plugins.Cashu",
                columns: table => new
                {
                    StoreId = table.Column<string>(type: "text", nullable: false),
                    KeysetId = table.Column<string>(type: "text", nullable: false),
                    Counter = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreKeysetCounters", x => new { x.StoreId, x.KeysetId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashuWalletConfig",
                schema: "BTCPayServer.Plugins.Cashu");

            migrationBuilder.DropTable(
                name: "StoreKeysetCounters",
                schema: "BTCPayServer.Plugins.Cashu");

            migrationBuilder.DropColumn(
                name: "P2PkE",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs");
        }
    }
}
