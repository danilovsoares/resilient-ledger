using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Verity.Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reversal_of_transaction_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_reversal_of_transaction_id",
                table: "transactions",
                column: "reversal_of_transaction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_transactions_reversal_of_transaction_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "reversal_of_transaction_id",
                table: "transactions");
        }
    }
}
