using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MS.Microservice.Persistence.EFCore.Migrations.Activation
{
    /// <inheritdoc />
    public partial class AddOutboxTraceContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                schema: "fz_platform_activation",
                table: "OutboxMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceState",
                schema: "fz_platform_activation",
                table: "OutboxMessages",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TraceParent",
                schema: "fz_platform_activation",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "TraceState",
                schema: "fz_platform_activation",
                table: "OutboxMessages");
        }
    }
}
