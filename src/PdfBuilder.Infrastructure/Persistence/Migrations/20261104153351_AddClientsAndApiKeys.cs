using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PdfBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientsAndApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clients",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clients", x => x.id);
                    table.CheckConstraint("ck_clients_id_format", "id ~ '^[a-z][a-z0-9-]{0,39}$'");
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    key_id = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    client_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.key_id);
                    table.CheckConstraint("ck_api_keys_key_id_format", "key_id ~ '^[a-z0-9]{8}$'");
                    table.CheckConstraint("ck_api_keys_scopes_not_empty", "cardinality(scopes) > 0");
                    table.CheckConstraint("ck_api_keys_secret_hash_length", "octet_length(secret_hash) = 32");
                    table.ForeignKey(
                        name: "fk_api_keys_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_client_id",
                table: "api_keys",
                column: "client_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "clients");
        }
    }
}
