using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeroSwasthya.Modules.Clinical.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialClinical : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "clinical");

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    patient_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    taken_at = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    object_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    storage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    ai_summary = table.Column<string>(type: "text", nullable: true),
                    ai_summary_status = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "visits",
                schema: "clinical",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    patient_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    facility_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    facility_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    visit_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    chief_complaint_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    vitals = table.Column<string>(type: "jsonb", nullable: false),
                    diagnosis_codes = table.Column<List<string>>(type: "text[]", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    advice = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    follow_up_at = table.Column<DateOnly>(type: "date", nullable: true),
                    referral = table.Column<string>(type: "jsonb", nullable: true),
                    prescriptions = table.Column<string>(type: "jsonb", nullable: false),
                    supersedes_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visits", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_documents_patient_id_taken_at",
                schema: "clinical",
                table: "documents",
                columns: new[] { "patient_id", "taken_at" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_updated_at",
                schema: "clinical",
                table: "documents",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_visits_patient_id_visit_at",
                schema: "clinical",
                table: "visits",
                columns: new[] { "patient_id", "visit_at" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_updated_at",
                schema: "clinical",
                table: "visits",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documents",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "visits",
                schema: "clinical");
        }
    }
}
