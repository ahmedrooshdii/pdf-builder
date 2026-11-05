using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PdfBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "client_id",
                table: "pdf_jobs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "client_id",
                table: "authored_templates",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "client_id",
                table: "pdf_jobs");

            migrationBuilder.DropColumn(
                name: "client_id",
                table: "authored_templates");
        }
    }
}
