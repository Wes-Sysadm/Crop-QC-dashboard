using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldSampleEmailHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QcSummaryEmailLogs_QcSamples_QcSampleId",
                table: "QcSummaryEmailLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_QcSummaryEmailLogs_Receipts_ReceiptId",
                table: "QcSummaryEmailLogs");

            migrationBuilder.AlterColumn<long>(
                name: "ReceiptId",
                table: "QcSummaryEmailLogs",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddForeignKey(
                name: "FK_QcSummaryEmailLogs_QcSamples_QcSampleId",
                table: "QcSummaryEmailLogs",
                column: "QcSampleId",
                principalTable: "QcSamples",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QcSummaryEmailLogs_Receipts_ReceiptId",
                table: "QcSummaryEmailLogs",
                column: "ReceiptId",
                principalTable: "Receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QcSummaryEmailLogs_QcSamples_QcSampleId",
                table: "QcSummaryEmailLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_QcSummaryEmailLogs_Receipts_ReceiptId",
                table: "QcSummaryEmailLogs");

            migrationBuilder.AlterColumn<long>(
                name: "ReceiptId",
                table: "QcSummaryEmailLogs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_QcSummaryEmailLogs_QcSamples_QcSampleId",
                table: "QcSummaryEmailLogs",
                column: "QcSampleId",
                principalTable: "QcSamples",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_QcSummaryEmailLogs_Receipts_ReceiptId",
                table: "QcSummaryEmailLogs",
                column: "ReceiptId",
                principalTable: "Receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
