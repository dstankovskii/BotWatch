using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BotWatch.Incidents.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "check_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    probe = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_results", x => x.id);
                });

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
                name: "incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cause = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidents", x => x.id);
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
                name: "notification_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    bot_token = table.Column<byte[]>(type: "bytea", nullable: false),
                    chat_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_channels", x => x.id);
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
                name: "check_states",
                columns: table => new
                {
                    check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monitor_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    check_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    interval = table.Column<TimeSpan>(type: "interval", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    last_result_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    open_incident_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_states", x => x.check_id);
                    table.ForeignKey(
                        name: "fk_check_states_incidents_open_incident_id",
                        column: x => x.open_incident_id,
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_check_results_check_id_finished_at",
                table: "check_results",
                columns: new[] { "check_id", "finished_at" });

            migrationBuilder.CreateIndex(
                name: "ix_check_states_monitor_id",
                table: "check_states",
                column: "monitor_id");

            migrationBuilder.CreateIndex(
                name: "ix_check_states_open_incident_id",
                table: "check_states",
                column: "open_incident_id");

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_processed_at",
                table: "inbox_messages",
                column: "processed_at");

            migrationBuilder.CreateIndex(
                name: "ix_incidents_check_id_opened_at",
                table: "incidents",
                columns: new[] { "check_id", "opened_at" });

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
                name: "check_results");

            migrationBuilder.DropTable(
                name: "check_states");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "leases");

            migrationBuilder.DropTable(
                name: "notification_channels");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "incidents");
        }
    }
}
