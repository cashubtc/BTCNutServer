using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Cashu.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProofState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Proofs_FailedTransactions_FailedTransactionId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs");

            migrationBuilder.RenameColumn(
                name: "FailedTransactionId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs",
                newName: "ExportedTokenId");

            migrationBuilder.RenameIndex(
                name: "IX_Proofs_FailedTransactionId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs",
                newName: "IX_Proofs_ExportedTokenId");

            migrationBuilder.AlterColumn<long>(
                name: "Counter",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "StoreKeysetCounters",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "InputAmount",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "numeric(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "InputProofsJson",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: true);

            // delete old FailedTransactions - they have no InputProofsJson and are unusable
            migrationBuilder.Sql(@"
                DELETE FROM ""BTCPayServer.Plugins.Cashu"".""FailedTransactions""
                WHERE ""InputProofsJson"" IS NULL;
            ");

            // delete orphaned proofs (were linked to failed transactions via old FailedTransactionId)
            // after rename to ExportedTokenId, these point to non-existent ExportedTokens
            migrationBuilder.Sql(@"
                DELETE FROM ""BTCPayServer.Plugins.Cashu"".""Proofs""
                WHERE ""ExportedTokenId"" IS NOT NULL
                AND NOT EXISTS (
                    SELECT 1 FROM ""BTCPayServer.Plugins.Cashu"".""ExportedTokens""
                    WHERE ""Id"" = ""Proofs"".""ExportedTokenId""
                );
            ");
            

            migrationBuilder.CreateIndex(
                name: "IX_Proofs_Status",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_Proofs_ExportedTokens_ExportedTokenId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs",
                column: "ExportedTokenId",
                principalSchema: "BTCPayServer.Plugins.Cashu",
                principalTable: "ExportedTokens",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Proofs_ExportedTokens_ExportedTokenId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs");

            migrationBuilder.DropIndex(
                name: "IX_Proofs_Status",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs");

            migrationBuilder.DropColumn(
                name: "InputAmount",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions");

            migrationBuilder.DropColumn(
                name: "InputProofsJson",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions");

            migrationBuilder.RenameColumn(
                name: "ExportedTokenId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs",
                newName: "FailedTransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_Proofs_ExportedTokenId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs",
                newName: "IX_Proofs_FailedTransactionId");

            migrationBuilder.AlterColumn<int>(
                name: "Counter",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "StoreKeysetCounters",
                type: "integer",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddForeignKey(
                name: "FK_Proofs_FailedTransactions_FailedTransactionId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs",
                column: "FailedTransactionId",
                principalSchema: "BTCPayServer.Plugins.Cashu",
                principalTable: "FailedTransactions",
                principalColumn: "Id");
        }
    }
}
