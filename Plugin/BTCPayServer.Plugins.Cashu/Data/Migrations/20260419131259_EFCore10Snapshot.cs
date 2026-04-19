using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Cashu.Data.Migrations
{
    /// <inheritdoc />
    public partial class EFCore10Snapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "StoreId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Mints",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Unit",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "MintKeys",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Keyset",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "MintKeys",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StoreId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "QuoteState",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "QuoteId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Preimage",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PaymentHash",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Mint",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Bolt11",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "Amount",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StoreId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "QuoteState",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "QuoteId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OutputData",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Mint",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "KeysetId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "Amount",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Unit",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StoreId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OutputData",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MintUrl",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Unit",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "ExportedTokens",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StoreId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "ExportedTokens",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SerializedToken",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "ExportedTokens",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Mint",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "ExportedTokens",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "StoreId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Proofs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "Mints",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Unit",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "MintKeys",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Keyset",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "MintKeys",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "StoreId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "QuoteState",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "QuoteId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Preimage",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "PaymentHash",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Mint",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Bolt11",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<long>(
                name: "Amount",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningPayments",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "StoreId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "QuoteState",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "QuoteId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "OutputData",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Mint",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "KeysetId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<long>(
                name: "Amount",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "LightningInvoices",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "Unit",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "StoreId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "OutputData",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "MintUrl",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "InvoiceId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Unit",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "ExportedTokens",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "StoreId",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "ExportedTokens",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "SerializedToken",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "ExportedTokens",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Mint",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "ExportedTokens",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
