using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotWatch.Monitoring.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PublicMonitors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_public",
                table: "monitors",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_public",
                table: "monitors");
        }
    }
}
