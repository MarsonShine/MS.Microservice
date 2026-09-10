using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MS.Microservice.Persistence.EFCore.Migrations.Activation
{
    /// <inheritdoc />
    public partial class AddInboxMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "fz_platform_activation",
                columns: table => new
                {
                    DeduplicationKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MessageType = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessingStartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DuplicateCount = table.Column<int>(type: "integer", nullable: false),
                    LastDuplicateAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.DeduplicationKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_MessageId_Consumer",
                schema: "fz_platform_activation",
                table: "InboxMessages",
                columns: new[] { "MessageId", "Consumer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_Status_ReceivedAtUtc",
                schema: "fz_platform_activation",
                table: "InboxMessages",
                columns: new[] { "Status", "ReceivedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "fz_platform_activation");
        }
    }
}
