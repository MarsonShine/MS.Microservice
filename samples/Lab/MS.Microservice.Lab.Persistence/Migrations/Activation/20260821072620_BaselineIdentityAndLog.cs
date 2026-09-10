using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MS.Microservice.Persistence.EFCore.Migrations.Activation
{
    /// <inheritdoc />
    public partial class BaselineIdentityAndLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fz_platform_activation");

            migrationBuilder.CreateTable(
                name: "Actions",
                schema: "fz_platform_activation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    Path = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Actions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Logs",
                schema: "fz_platform_activation",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventName = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    Type = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    MethodName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false),
                    CreatorId = table.Column<int>(type: "integer", nullable: false),
                    IP = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Telephone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "fz_platform_activation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    Description = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "fz_platform_activation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Account = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    Name = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    Password = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Salt = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Telephone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: true),
                    CreatorId = table.Column<int>(type: "integer", nullable: false),
                    UpdatorId = table.Column<int>(type: "integer", nullable: false),
                    FzAccount = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FzId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoleActions",
                schema: "fz_platform_activation",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    ActionId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleActions", x => new { x.RoleId, x.ActionId });
                    table.ForeignKey(
                        name: "FK_RoleActions_Actions_ActionId",
                        column: x => x.ActionId,
                        principalSchema: "fz_platform_activation",
                        principalTable: "Actions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoleActions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "fz_platform_activation",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                schema: "fz_platform_activation",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "fz_platform_activation",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "fz_platform_activation",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Logs_CreatedAt",
                schema: "fz_platform_activation",
                table: "Logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Logs_CreatorId",
                schema: "fz_platform_activation",
                table: "Logs",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Logs_CreatorId_CreatedAt_IP_MethodName",
                schema: "fz_platform_activation",
                table: "Logs",
                columns: new[] { "CreatorId", "CreatedAt", "IP", "MethodName" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleActions_ActionId",
                schema: "fz_platform_activation",
                table: "RoleActions",
                column: "ActionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                schema: "fz_platform_activation",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Account",
                schema: "fz_platform_activation",
                table: "Users",
                column: "Account",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_DeletedAt",
                schema: "fz_platform_activation",
                table: "Users",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Users_FzAccount",
                schema: "fz_platform_activation",
                table: "Users",
                column: "FzAccount",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Logs",
                schema: "fz_platform_activation");

            migrationBuilder.DropTable(
                name: "RoleActions",
                schema: "fz_platform_activation");

            migrationBuilder.DropTable(
                name: "UserRoles",
                schema: "fz_platform_activation");

            migrationBuilder.DropTable(
                name: "Actions",
                schema: "fz_platform_activation");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "fz_platform_activation");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "fz_platform_activation");
        }
    }
}
