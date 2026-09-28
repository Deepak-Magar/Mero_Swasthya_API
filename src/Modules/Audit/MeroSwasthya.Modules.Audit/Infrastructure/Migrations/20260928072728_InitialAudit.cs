using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MeroSwasthya.Modules.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    patient_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actor_facility_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_user_id_patient_id_action_at",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "actor_user_id", "patient_id", "action", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_patient_id_at",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "patient_id", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "audit");
        }
    }
}
