using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeroSwasthya.Modules.Grants.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "grants");

            migrationBuilder.CreateTable(
                name: "access_grants",
                schema: "grants",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    patient_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    sections = table.Column<List<string>>(type: "text[]", nullable: false),
                    jti = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    long_lived = table.Column<bool>(type: "boolean", nullable: false),
                    redeemed_by_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    redeemed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    access_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_grants", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_grants_jti",
                schema: "grants",
                table: "access_grants",
                column: "jti",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_grants_patient_id_created_at",
                schema: "grants",
                table: "access_grants",
                columns: new[] { "patient_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_access_grants_redeemed_by_user_id_patient_id",
                schema: "grants",
                table: "access_grants",
                columns: new[] { "redeemed_by_user_id", "patient_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_grants",
                schema: "grants");
        }
    }
}
