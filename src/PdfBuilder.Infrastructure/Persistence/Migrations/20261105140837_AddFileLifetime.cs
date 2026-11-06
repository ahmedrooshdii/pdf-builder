using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PdfBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileLifetime : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// The order matters: the columns first, then the backfill, and only then the constraint that the
        /// backfilled rows satisfy. A job that succeeded before this migration was created when it completed.
        /// The stored link is dropped last: links are made per read from the file id (feature 005).
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "file_created_at",
                table: "pdf_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "file_expired_at",
                table: "pdf_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE pdf_jobs SET file_created_at = completed_at WHERE status = 'Succeeded';");

            migrationBuilder.CreateIndex(
                name: "ix_pdf_jobs_file_created_at",
                table: "pdf_jobs",
                column: "file_created_at");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pdf_jobs_file_created_at",
                table: "pdf_jobs",
                sql: "status <> 'Succeeded' OR file_created_at IS NOT NULL");

            migrationBuilder.DropColumn(
                name: "file_url",
                table: "pdf_jobs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pdf_jobs_file_created_at",
                table: "pdf_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pdf_jobs_file_created_at",
                table: "pdf_jobs");

            migrationBuilder.DropColumn(
                name: "file_created_at",
                table: "pdf_jobs");

            migrationBuilder.DropColumn(
                name: "file_expired_at",
                table: "pdf_jobs");

            migrationBuilder.AddColumn<string>(
                name: "file_url",
                table: "pdf_jobs",
                type: "text",
                nullable: true);
        }
    }
}
