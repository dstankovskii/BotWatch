using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BotWatch.Monitoring.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.message_id, x.consumer });
                });

            migrationBuilder.CreateTable(
                name: "leases",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leases", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "monitors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    exchange = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    routing_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "checks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    interval = table.Column<TimeSpan>(type: "interval", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    http_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    http_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    http_expected_status = table.Column<int>(type: "integer", nullable: true),
                    http_body_contains = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    http_timeout = table.Column<TimeSpan>(type: "interval", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checks", x => x.id);
                    table.ForeignKey(
                        name: "fk_checks_monitors_monitor_id",
                        column: x => x.monitor_id,
                        principalTable: "monitors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_checks_monitor_id",
                table: "checks",
                column: "monitor_id");

            migrationBuilder.CreateIndex(
                name: "ix_checks_next_run_at",
                table: "checks",
                column: "next_run_at",
                filter: "enabled");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_processed_at",
                table: "inbox_messages",
                column: "processed_at");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_position",
                table: "outbox_messages",
                column: "position",
                filter: "processed_at is null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checks");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "leases");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "monitors");
        }
    }
}
