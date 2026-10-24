using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotWatch.Monitoring.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TelegramChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                table: "checks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scenario",
                table: "checks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "telegram_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    telegram_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: true),
                    login_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    login_url_issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    throttled_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    budget_window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    budget_used = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_accounts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_checks_account_id",
                table: "checks",
                column: "account_id",
                filter: "account_id is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_accounts");

            migrationBuilder.DropIndex(
                name: "ix_checks_account_id",
                table: "checks");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "checks");

            migrationBuilder.DropColumn(
                name: "scenario",
                table: "checks");
        }
    }
}
