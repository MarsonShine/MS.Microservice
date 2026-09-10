using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MS.Microservice.Reference.Persistence.Migrations.SelfManaged
{
    /// <inheritdoc />
    public partial class BaselineReferenceSelfManaged : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.EnsureSchema(
                name: "reference");

            migrationBuilder.CreateTable(
                name: "Inbox",
                schema: "messaging",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContractName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContractVersion = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<long>(type: "bigint", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TraceParent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TraceState = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    LockToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inbox", x => new { x.MessageId, x.Consumer });
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContractVersion = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<long>(type: "bigint", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TraceParent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TraceState = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    LockToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfileAudit",
                schema: "reference",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileVersion = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorIssuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OccurredAtUtc = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileAudit", x => new { x.MessageId, x.Consumer });
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfileRoles",
                schema: "reference",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileRoles", x => new { x.ProfileId, x.Name });
                    table.ForeignKey(
                        name: "FK_ProfileRoles_UserProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalSchema: "reference",
                        principalTable: "UserProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_CompletedAtUtc",
                schema: "messaging",
                table: "Inbox",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Inbox_State_NextAttemptAtUtc",
                schema: "messaging",
                table: "Inbox",
                columns: new[] { "State", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_CompletedAtUtc",
                schema: "messaging",
                table: "Outbox",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_LockedUntilUtc",
                schema: "messaging",
                table: "Outbox",
                column: "LockedUntilUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_State_NextAttemptAtUtc",
                schema: "messaging",
                table: "Outbox",
                columns: new[] { "State", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileAudit_ProfileId_ProfileVersion_Consumer",
                schema: "reference",
                table: "ProfileAudit",
                columns: new[] { "ProfileId", "ProfileVersion", "Consumer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_Issuer_Subject",
                schema: "reference",
                table: "UserProfiles",
                columns: new[] { "Issuer", "Subject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Inbox",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "ProfileAudit",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "ProfileRoles",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "UserProfiles",
                schema: "reference");
        }
    }
}
