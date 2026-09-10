using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MS.Microservice.Persistence.EFCore.Migrations.Activation
{
    /// <inheritdoc />
    public partial class AddInboxProcessingLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProcessingLeaseExpiresAtUtc",
                schema: "fz_platform_activation",
                table: "InboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingToken",
                schema: "fz_platform_activation",
                table: "InboxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_ProcessingLeaseExpiresAtUtc",
                schema: "fz_platform_activation",
                table: "InboxMessages",
                column: "ProcessingLeaseExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InboxMessages_ProcessingLeaseExpiresAtUtc",
                schema: "fz_platform_activation",
                table: "InboxMessages");

            migrationBuilder.DropColumn(
                name: "ProcessingLeaseExpiresAtUtc",
                schema: "fz_platform_activation",
                table: "InboxMessages");

            migrationBuilder.DropColumn(
                name: "ProcessingToken",
                schema: "fz_platform_activation",
                table: "InboxMessages");
        }
    }
}
