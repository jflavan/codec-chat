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
                        name: "FK_ServerMemberRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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

            // 3. Drop the old RoleId column from ServerMembers (now replaced by ServerMemberRoles join table)
            migrationBuilder.DropForeignKey(
                name: "FK_ServerMembers_ServerRoles_RoleId",
                table: "ServerMembers");

            migrationBuilder.DropIndex(
                name: "IX_ServerMembers_RoleId",
                table: "ServerMembers");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "ServerMembers");

            // 4. Create ChannelPermissionOverrides table
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

            // Restore the RoleId column on ServerMembers before dropping ServerMemberRoles
            migrationBuilder.AddColumn<Guid>(
                name: "RoleId",
                table: "ServerMembers",
                type: "uuid",
                nullable: true);

            // Migrate data back from ServerMemberRoles to ServerMembers.RoleId (pick highest-priority role)
            migrationBuilder.Sql(@"
                UPDATE ""ServerMembers"" sm
                SET ""RoleId"" = (
                    SELECT smr.""RoleId"" FROM ""ServerMemberRoles"" smr
                    JOIN ""ServerRoles"" sr ON sr.""Id"" = smr.""RoleId""
                    WHERE smr.""UserId"" = sm.""UserId""
                    ORDER BY sr.""Position"" ASC
                    LIMIT 1
                );
            ");

            // For any members without roles, assign the server's Member system role
            migrationBuilder.Sql(@"
                UPDATE ""ServerMembers"" sm
                SET ""RoleId"" = (
                    SELECT sr.""Id"" FROM ""ServerRoles"" sr
                    WHERE sr.""ServerId"" = sm.""ServerId"" AND sr.""IsSystemRole"" = true AND sr.""Name"" = 'Member'
                    LIMIT 1
                )
                WHERE sm.""RoleId"" IS NULL;
            ");

            // Now make the column non-nullable
            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "ServerMembers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_ServerMembers_RoleId",
                table: "ServerMembers",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServerMembers_ServerRoles_RoleId",
                table: "ServerMembers",
                column: "RoleId",
                principalTable: "ServerRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropTable(
                name: "ServerMemberRoles");
        }
    }
}
