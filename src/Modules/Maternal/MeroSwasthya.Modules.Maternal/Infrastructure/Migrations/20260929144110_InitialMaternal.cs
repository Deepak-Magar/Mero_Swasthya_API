using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeroSwasthya.Modules.Maternal.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMaternal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "maternal");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
