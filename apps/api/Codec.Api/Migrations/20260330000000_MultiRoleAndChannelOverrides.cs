using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class MultiRoleAndChannelOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create ServerMemberRoles join table
            migrationBuilder.CreateTable(
                name: "ServerMemberRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerMemberRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_ServerMemberRoles_ServerRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "ServerRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerMemberRoles_RoleId",
                table: "ServerMemberRoles",
                column: "RoleId");

            // 2. Migrate existing data: seed ServerMemberRoles from current RoleId
            migrationBuilder.Sql(@"
                INSERT INTO ""ServerMemberRoles"" (""UserId"", ""RoleId"", ""AssignedAt"")
                SELECT ""UserId"", ""RoleId"", ""JoinedAt""
                FROM ""ServerMembers""
                WHERE ""RoleId"" IS NOT NULL;
            ");

            // 3. Create ChannelPermissionOverrides table
            migrationBuilder.CreateTable(
                name: "ChannelPermissionOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Allow = table.Column<long>(type: "bigint", nullable: false),
                    Deny = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelPermissionOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelPermissionOverrides_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelPermissionOverrides_ServerRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "ServerRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelPermissionOverrides_ChannelId_RoleId",
                table: "ChannelPermissionOverrides",
                columns: new[] { "ChannelId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelPermissionOverrides_RoleId",
                table: "ChannelPermissionOverrides",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelPermissionOverrides");

            migrationBuilder.DropTable(
                name: "ServerMemberRoles");
        }
    }
}
