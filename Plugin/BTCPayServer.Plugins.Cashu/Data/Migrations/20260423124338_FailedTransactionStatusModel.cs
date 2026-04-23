using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.Cashu.Data.Migrations
{
    /// <inheritdoc />
    public partial class FailedTransactionStatusModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DismissedAt",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "text",
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.Sql("""
                UPDATE "BTCPayServer.Plugins.Cashu"."FailedTransactions"
                SET "CreatedAt" = COALESCE("LastRetried", NOW()),
                    "Status" = CASE
                        WHEN NOT "Resolved" THEN 'Pending'
                        WHEN "Details" = 'Resolved by poller.' THEN 'Recovered'
                        ELSE 'NeedsManualReview'
                    END,
                    "ReasonCode" = CASE
                        WHEN NOT "Resolved" THEN 'legacy_pending'
                        WHEN "Details" = 'Resolved by poller.' THEN 'resolved_by_poller'
                        ELSE 'legacy_unknown'
                    END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_FailedTransactions_CreatedAt",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FailedTransactions_Status",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                column: "Status");

            migrationBuilder.DropColumn(
                name: "Resolved",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Resolved",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE "BTCPayServer.Plugins.Cashu"."FailedTransactions"
                SET "Resolved" = CASE
                    WHEN "Status" = 'Pending' THEN FALSE
                    ELSE TRUE
                END;
                """);

            migrationBuilder.DropIndex(
                name: "IX_FailedTransactions_CreatedAt",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions");

            migrationBuilder.DropIndex(
                name: "IX_FailedTransactions_Status",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions");

            migrationBuilder.DropColumn(
                name: "DismissedAt",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "BTCPayServer.Plugins.Cashu",
                table: "FailedTransactions");
        }
    }
}
