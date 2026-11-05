using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PdfBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequireClientOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every row must have an owner by now: the service assigns them between AddClientOwner and this
            // migration (ClientOwnerAssignment). There is deliberately no default value. The generated one
            // ("") would have written an owner that means "unknown" into any row left over, and the foreign
            // keys below would then fail with a message about it. Without it, a row with no owner makes
            // SET NOT NULL fail, which says what is wrong, and the database is left as it was.
            migrationBuilder.DropPrimaryKey(
                name: "pk_authored_templates",
                table: "authored_templates");

            migrationBuilder.DropIndex(
                name: "ux_authored_templates_key_lower",
                table: "authored_templates");

            migrationBuilder.AlterColumn<string>(
                name: "client_id",
                table: "pdf_jobs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "client_id",
                table: "authored_templates",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_authored_templates",
                table: "authored_templates",
                columns: new[] { "client_id", "key" });

            migrationBuilder.CreateIndex(
                name: "ix_pdf_jobs_client_id",
                table: "pdf_jobs",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ux_authored_templates_client_key_lower",
                table: "authored_templates",
                columns: new[] { "client_id", "key_lower" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_authored_templates_clients_client_id",
                table: "authored_templates",
                column: "client_id",
                principalTable: "clients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pdf_jobs_clients_client_id",
                table: "pdf_jobs",
                column: "client_id",
                principalTable: "clients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_authored_templates_clients_client_id",
                table: "authored_templates");

            migrationBuilder.DropForeignKey(
                name: "fk_pdf_jobs_clients_client_id",
                table: "pdf_jobs");

            migrationBuilder.DropIndex(
                name: "ix_pdf_jobs_client_id",
                table: "pdf_jobs");

            migrationBuilder.DropPrimaryKey(
                name: "pk_authored_templates",
                table: "authored_templates");

            migrationBuilder.DropIndex(
                name: "ux_authored_templates_client_key_lower",
                table: "authored_templates");

            migrationBuilder.AlterColumn<string>(
                name: "client_id",
                table: "pdf_jobs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "client_id",
                table: "authored_templates",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AddPrimaryKey(
                name: "pk_authored_templates",
                table: "authored_templates",
                column: "key");

            migrationBuilder.CreateIndex(
                name: "ux_authored_templates_key_lower",
                table: "authored_templates",
                column: "key_lower",
                unique: true);
        }
    }
}
