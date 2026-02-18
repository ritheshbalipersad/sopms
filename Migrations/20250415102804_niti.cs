using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOPMSApp.Migrations
{
    /// <inheritdoc />
    public partial class niti : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocType",
                table: "DocRegisters",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocType",
                table: "DocRegisters");
        }
    }
}
