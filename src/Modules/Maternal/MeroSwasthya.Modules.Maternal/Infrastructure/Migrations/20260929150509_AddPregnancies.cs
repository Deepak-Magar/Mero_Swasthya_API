using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeroSwasthya.Modules.Maternal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPregnancies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "maternal");

            migrationBuilder.CreateTable(
                name: "anc_contacts",
                schema: "maternal",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pregnancy_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    patient_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    contact_no = table.Column<int>(type: "integer", nullable: false),
                    week_target = table.Column<int>(type: "integer", nullable: false),
                    due_at = table.Column<DateOnly>(type: "date", nullable: false),
                    done_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    provider_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    findings = table.Column<string>(type: "jsonb", nullable: true),
                    danger_signs = table.Column<List<string>>(type: "text[]", nullable: false),
                    triage_level = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    triage_reasons = table.Column<List<string>>(type: "text[]", nullable: false),
                    referral = table.Column<string>(type: "jsonb", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_anc_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pregnancies",
                schema: "maternal",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    patient_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    lmp = table.Column<DateOnly>(type: "date", nullable: true),
                    edd = table.Column<DateOnly>(type: "date", nullable: false),
                    gravida = table.Column<int>(type: "integer", nullable: false),
                    para = table.Column<int>(type: "integer", nullable: false),
                    risk_factors = table.Column<List<string>>(type: "text[]", nullable: false),
                    risk_level = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    birth_plan = table.Column<string>(type: "jsonb", nullable: true),
                    registered_by_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pregnancies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anc_contacts_patient_id_done_at",
                schema: "maternal",
                table: "anc_contacts",
                columns: new[] { "patient_id", "done_at" });

            migrationBuilder.CreateIndex(
                name: "ix_anc_contacts_pregnancy_id_contact_no",
                schema: "maternal",
                table: "anc_contacts",
                columns: new[] { "pregnancy_id", "contact_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_anc_contacts_updated_at",
                schema: "maternal",
                table: "anc_contacts",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_pregnancies_patient_id_status",
                schema: "maternal",
                table: "pregnancies",
                columns: new[] { "patient_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_pregnancies_updated_at",
                schema: "maternal",
                table: "pregnancies",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anc_contacts",
                schema: "maternal");

            migrationBuilder.DropTable(
                name: "pregnancies",
                schema: "maternal");
        }
    }
}
