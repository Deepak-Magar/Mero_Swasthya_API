using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeroSwasthya.Modules.Patients.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPatients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "patients");

            migrationBuilder.CreateTable(
                name: "patients",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sex = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    dob = table.Column<DateOnly>(type: "date", nullable: false),
                    blood_group = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ward = table.Column<int>(type: "integer", nullable: true),
                    municipality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    allergies = table.Column<List<string>>(type: "text[]", nullable: false),
                    chronic_conditions = table.Column<List<string>>(type: "text[]", nullable: false),
                    emergency_contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patients", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_patients_owner_user_id",
                schema: "patients",
                table: "patients",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_patients_updated_at",
                schema: "patients",
                table: "patients",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patients",
                schema: "patients");
        }
    }
}
