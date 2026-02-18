using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOPMSApp.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionToDocRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SOPStatus",
                table: "DocRegisters");

            migrationBuilder.AddColumn<string>(
                name: "Revision",
                table: "DocRegisters",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revision",
                table: "DocRegisters");

            migrationBuilder.AddColumn<string>(
                name: "SOPStatus",
                table: "DocRegisters",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
