using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeroSwasthya.Modules.Reminders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reminders");

            migrationBuilder.CreateTable(
                name: "reminders",
                schema: "reminders",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    patient_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pregnancy_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    due_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    channel = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    recipient_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recipient_role = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    message_np = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    message_en = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    source_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reminders_patient_id_due_at",
                schema: "reminders",
                table: "reminders",
                columns: new[] { "patient_id", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reminders_pregnancy_id",
                schema: "reminders",
                table: "reminders",
                column: "pregnancy_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminders_source_key_recipient_phone",
                schema: "reminders",
                table: "reminders",
                columns: new[] { "source_key", "recipient_phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reminders_status_due_at",
                schema: "reminders",
                table: "reminders",
                columns: new[] { "status", "due_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reminders",
                schema: "reminders");
        }
    }
}
