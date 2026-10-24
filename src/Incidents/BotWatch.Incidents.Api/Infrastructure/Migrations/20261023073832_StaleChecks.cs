using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotWatch.Incidents.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StaleChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "watched_since",
                table: "check_states",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "watched_since",
                table: "check_states");
        }
    }
}
