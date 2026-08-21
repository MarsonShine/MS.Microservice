using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MS.Microservice.Infrastructure.EventSourcing.Migrations
{
    /// <inheritdoc />
    public partial class BaselineEventSourcing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "event_sourcing");

            migrationBuilder.CreateTable(
                name: "event_store",
                schema: "event_sourcing",
                columns: table => new
                {
                    GlobalPosition = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    StreamId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StreamType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_store", x => x.GlobalPosition);
                });

            migrationBuilder.CreateTable(
                name: "order_read_model",
                schema: "event_sourcing",
                columns: table => new
                {
                    OrderId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ItemCount = table.Column<int>(type: "integer", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_read_model", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "projection_checkpoint",
                schema: "event_sourcing",
                columns: table => new
                {
                    ProjectionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastGlobalPosition = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projection_checkpoint", x => x.ProjectionName);
                });

            migrationBuilder.CreateTable(
                name: "snapshots",
                schema: "event_sourcing",
                columns: table => new
                {
                    StreamId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StreamType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_snapshots", x => x.StreamId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_store_CreatedAt",
                schema: "event_sourcing",
                table: "event_store",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_event_store_EventId",
                schema: "event_sourcing",
                table: "event_store",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_store_StreamId",
                schema: "event_sourcing",
                table: "event_store",
                column: "StreamId");

            migrationBuilder.CreateIndex(
                name: "IX_event_store_StreamId_Version",
                schema: "event_sourcing",
                table: "event_store",
                columns: new[] { "StreamId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_store_StreamType",
                schema: "event_sourcing",
                table: "event_store",
                column: "StreamType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_store",
                schema: "event_sourcing");

            migrationBuilder.DropTable(
                name: "order_read_model",
                schema: "event_sourcing");

            migrationBuilder.DropTable(
                name: "projection_checkpoint",
                schema: "event_sourcing");

            migrationBuilder.DropTable(
                name: "snapshots",
                schema: "event_sourcing");
        }
    }
}
