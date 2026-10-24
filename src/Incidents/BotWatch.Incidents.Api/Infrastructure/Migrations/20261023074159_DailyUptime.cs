using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotWatch.Incidents.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DailyUptime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_uptime",
                columns: table => new
                {
                    check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    failed = table.Column<int>(type: "integer", nullable: false),
                    avg_duration_ms = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_uptime", x => new { x.check_id, x.day });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_uptime");
        }
    }
}
