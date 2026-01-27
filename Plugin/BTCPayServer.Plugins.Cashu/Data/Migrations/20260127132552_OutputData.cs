using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Cashu.Data.Migrations
{
    /// <inheritdoc />
    public partial class OutputData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutputData_BlindedMessages",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions");

            migrationBuilder.DropColumn(
                name: "OutputData_BlindingFactors",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions");

            migrationBuilder.RenameColumn(
                name: "OutputData_Secrets",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                newName: "OutputData");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OutputData",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                newName: "OutputData_Secrets");

            migrationBuilder.AddColumn<string>(
                name: "OutputData_BlindedMessages",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutputData_BlindingFactors",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: true);
        }
    }
}
