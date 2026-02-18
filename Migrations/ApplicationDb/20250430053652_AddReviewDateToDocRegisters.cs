using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOPMSApp.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddReviewDateToDocRegisters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "lastReviewDate",
                table: "DocRegisters",
                newName: "LastReviewDate");

            migrationBuilder.AlterColumn<DateTime>(
                name: "LastReviewDate",
                table: "DocRegisters",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LastReviewDate",
                table: "DocRegisters",
                newName: "lastReviewDate");

            migrationBuilder.AlterColumn<DateTime>(
                name: "lastReviewDate",
                table: "DocRegisters",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }
    }
}
