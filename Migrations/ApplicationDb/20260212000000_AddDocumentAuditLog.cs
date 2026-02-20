using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOPMSApp.Migrations.ApplicationDb
{
    /// <inheritdoc />
    public partial class AddDocumentAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocRegisterId = table.Column<int>(type: "int", nullable: true),
                    SopNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PerformedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PerformedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DocumentTitle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(name: "IX_DocumentAuditLogs_DocRegisterId", table: "DocumentAuditLogs", column: "DocRegisterId");
            migrationBuilder.CreateIndex(name: "IX_DocumentAuditLogs_PerformedAtUtc", table: "DocumentAuditLogs", column: "PerformedAtUtc");
            migrationBuilder.CreateIndex(name: "IX_DocumentAuditLogs_SopNumber", table: "DocumentAuditLogs", column: "SopNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DocumentAuditLogs");
        }
    }
}
