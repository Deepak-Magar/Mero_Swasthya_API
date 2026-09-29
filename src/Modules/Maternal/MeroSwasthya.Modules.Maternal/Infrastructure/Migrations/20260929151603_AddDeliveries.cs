using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeroSwasthya.Modules.Maternal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "maternal",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pregnancy_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    patient_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    delivered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    place = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    mode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    baby_weight_kg = table.Column<double>(type: "double precision", nullable: true),
                    baby_sex = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    complications = table.Column<List<string>>(type: "text[]", nullable: false),
                    recorded_by_user_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deliveries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_patient_id_delivered_at",
                schema: "maternal",
                table: "deliveries",
                columns: new[] { "patient_id", "delivered_at" });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_pregnancy_id",
                schema: "maternal",
                table: "deliveries",
                column: "pregnancy_id");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_updated_at",
                schema: "maternal",
                table: "deliveries",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "maternal");
        }
    }
}
