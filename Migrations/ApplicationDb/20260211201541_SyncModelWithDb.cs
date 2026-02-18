using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOPMSApp.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class SyncModelWithDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "StructuredSops",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Revision",
                table: "StructuredSops",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ReturnedDate",
                table: "StructuredSops",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<bool>(
                name: "AdminApproved",
                table: "StructuredSops",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdminApprovedDate",
                table: "StructuredSops",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStage",
                table: "StructuredSops",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSyncedToDocRegister",
                table: "StructuredSops",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ManagerApproved",
                table: "StructuredSops",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManagerApprovedDate",
                table: "StructuredSops",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "StructuredSops",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncedDate",
                table: "StructuredSops",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "uniqueNumber",
                table: "DocRegisters",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<bool>(
                name: "AdminApproved",
                table: "DocRegisters",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdminApprovedDate",
                table: "DocRegisters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStage",
                table: "DocRegisters",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Changed",
                table: "DocRegisters",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentPath",
                table: "DocRegisters",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsStructured",
                table: "DocRegisters",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ManagerApproved",
                table: "DocRegisters",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManagerApprovedDate",
                table: "DocRegisters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "DocRegisters",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedDate",
                table: "DocRegisters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StructuredSopId",
                table: "DocRegisters",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoPath",
                table: "DocRegisters",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeDescription",
                table: "DocRegisterHistories",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "DocArchives",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "UserEmail",
                table: "DeletedFileLogs",
                type: "nvarchar(500)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "DeletedOn",
                table: "DeletedFileLogs",
                type: "datetime",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedOn",
                table: "DeletedFileLogs",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Area",
                table: "DeletedFileLogs",
                type: "nvarchar(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Author",
                table: "DeletedFileLogs",
                type: "nvarchar(150)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "DeletedFileLogs",
                type: "nvarchar(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "DeletedFileLogs",
                type: "nvarchar(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepartmentSupervisor",
                table: "DeletedFileLogs",
                type: "nvarchar(150)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocType",
                table: "DeletedFileLogs",
                type: "nvarchar(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveDate",
                table: "DeletedFileLogs",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "DeletedFileLogs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "OriginalDocRegisterId",
                table: "DeletedFileLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Revision",
                table: "DeletedFileLogs",
                type: "nvarchar(50)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "DeletedFileLogs",
                type: "nvarchar(50)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupervisorEmail",
                table: "DeletedFileLogs",
                type: "nvarchar(150)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UniqueNumber",
                table: "DeletedFileLogs",
                type: "nvarchar(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UploadDate",
                table: "DeletedFileLogs",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasApproved",
                table: "DeletedFileLogs",
                type: "bit",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SopStepHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StepId = table.Column<int>(type: "int", nullable: true),
                    SopId = table.Column<int>(type: "int", nullable: true),
                    PropertyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedByEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SopStepHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StructuredSopHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SopId = table.Column<int>(type: "int", nullable: true),
                    PropertyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedByEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StructuredSopHistory", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SopStepHistory");

            migrationBuilder.DropTable(
                name: "StructuredSopHistory");

            migrationBuilder.DropColumn(
                name: "AdminApproved",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "AdminApprovedDate",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "ApprovalStage",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "IsSyncedToDocRegister",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "ManagerApproved",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "ManagerApprovedDate",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "SyncedDate",
                table: "StructuredSops");

            migrationBuilder.DropColumn(
                name: "AdminApproved",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "AdminApprovedDate",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "ApprovalStage",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "Changed",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "DocumentPath",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "IsStructured",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "ManagerApproved",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "ManagerApprovedDate",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "ReturnedDate",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "StructuredSopId",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "VideoPath",
                table: "DocRegisters");

            migrationBuilder.DropColumn(
                name: "ChangeDescription",
                table: "DocRegisterHistories");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "DocArchives");

            migrationBuilder.DropColumn(
                name: "ArchivedOn",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "Area",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "Author",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "Department",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "DepartmentSupervisor",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "DocType",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "EffectiveDate",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "OriginalDocRegisterId",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "SupervisorEmail",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "UniqueNumber",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "UploadDate",
                table: "DeletedFileLogs");

            migrationBuilder.DropColumn(
                name: "WasApproved",
                table: "DeletedFileLogs");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "StructuredSops",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "Revision",
                table: "StructuredSops",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ReturnedDate",
                table: "StructuredSops",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "uniqueNumber",
                table: "DocRegisters",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "UserEmail",
                table: "DeletedFileLogs",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "DeletedOn",
                table: "DeletedFileLogs",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime");
        }
    }
}
