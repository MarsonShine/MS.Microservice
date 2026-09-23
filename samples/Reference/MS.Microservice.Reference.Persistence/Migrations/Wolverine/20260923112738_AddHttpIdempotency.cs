using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MS.Microservice.Reference.Persistence.Migrations.Wolverine
{
    /// <inheritdoc />
    public partial class AddHttpIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HttpIdempotency",
                schema: "reference",
                columns: table => new
                {
                    ScopeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Location = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Body = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HttpIdempotency", x => new { x.ScopeHash, x.KeyHash });
                });

            migrationBuilder.CreateIndex(
                name: "IX_HttpIdempotency_ExpiresAtUtcTicks",
                schema: "reference",
                table: "HttpIdempotency",
                column: "ExpiresAtUtcTicks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HttpIdempotency",
                schema: "reference");
        }
    }
}
