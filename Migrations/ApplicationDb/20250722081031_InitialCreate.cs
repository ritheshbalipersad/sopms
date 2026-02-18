using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOPMSApp.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "StructuredSops",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "SopNumber",
                table: "StructuredSops",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Revision",
                table: "StructuredSops",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ControlledBy",
                table: "StructuredSops",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ApprovedBy",
                table: "StructuredSops",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedOn",
                table: "StructuredSops",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Area",
                table: "StructuredSops",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDate",
                table: "StructuredSops",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "DocRegisterId",
                table: "StructuredSops",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocType",
                table: "StructuredSops",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "StructuredSops",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "StructuredSops",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "StructuredSops",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "KeyPointImagePath",
                table: "SopSteps",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "ReviewedBy",
                table: "DocRegisters",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "DocRegisters",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedOn",
                table: "DocRegisters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "DocRegisters",
                type: "bit",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocArchives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SopNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Revision = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DocType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Area = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArchivedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ArchivedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceTable = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocArchives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocRegisterHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocRegisterId = table.Column<int>(type: "int", nullable: false),
                    SopNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalFile = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Department = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revision = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReviewDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UploadDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RevisedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RevisedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocRegisterHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocRegisterHistories_DocRegisters_DocRegisterId",
                        column: x => x.DocRegisterId,
                        principalTable: "DocRegisters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StructuredSops_DocRegisterId",
                table: "StructuredSops",
                column: "DocRegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_DocRegisterHistories_DocRegisterId",
                table: "DocRegisterHistories",
                column: "DocRegisterId");

            migrationBuilder.AddForeignKey(
                name: "FK_StructuredSops_DocRegisters_DocRegisterId",
                table: "StructuredSops",
                column: "DocRegisterId",
                principalTable: "DocRegisters",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StructuredSops_DocRegisters_DocRegisterId",
                table: "StructuredSops");

            migrationBuilder.DropTable(
                name: "DocArchives");

            migrationBuilder.DropTable(
                name: "DocRegisterHistories");

            migrationBuilder.DropIndex(
                name: "IX_StructuredSops_DocRegisterId",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "ArchivedOn",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "Area",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "CreatedDate",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "DocRegisterId",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "DocType",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "KeyPointImagePath",
                table: "SopSteps");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "ArchivedOn",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "DocRegisters");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "StructuredSops",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "SopNumber",
                table: "StructuredSops",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Revision",
                table: "StructuredSops",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "ControlledBy",
                table: "StructuredSops",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "ApprovedBy",
                table: "StructuredSops",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReviewedBy",
                table: "DocRegisters",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
