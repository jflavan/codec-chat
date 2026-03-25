using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class CustomRolesAndPermissions : Migration
    {
        // Permission flag values (must match Permission enum)
        private const long Administrator = 1L << 40;
        private const long AdminDefaults =
            (1L << 0) | (1L << 1) | (1L << 2) | (1L << 3) | (1L << 4) | (1L << 5) |
            (1L << 6) | (1L << 7) | (1L << 10) | (1L << 20) | (1L << 21) | (1L << 22) |
            (1L << 23) | (1L << 24) | (1L << 25) | (1L << 26) | (1L << 30) | (1L << 31) |
            (1L << 32) | (1L << 33);
        private const long MemberDefaults =
            (1L << 0) | (1L << 20) | (1L << 21) | (1L << 22) | (1L << 23) | (1L << 6) |
            (1L << 30) | (1L << 31);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create ServerRoles table
            migrationBuilder.CreateTable(
                name: "ServerRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Permissions = table.Column<long>(type: "bigint", nullable: false),
                    IsSystemRole = table.Column<bool>(type: "boolean", nullable: false),
                    IsHoisted = table.Column<bool>(type: "boolean", nullable: false),
                    IsMentionable = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServerRoles_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerRoles_ServerId_Name",
                table: "ServerRoles",
                columns: new[] { "ServerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServerRoles_ServerId_Position",
                table: "ServerRoles",
                columns: new[] { "ServerId", "Position" });

            // 2. Create default roles for every existing server
            migrationBuilder.Sql($@"
                INSERT INTO ""ServerRoles"" (""Id"", ""ServerId"", ""Name"", ""Color"", ""Position"", ""Permissions"", ""IsSystemRole"", ""IsHoisted"", ""IsMentionable"", ""CreatedAt"")
                SELECT gen_random_uuid(), s.""Id"", 'Owner', NULL, 0, {Administrator}, true, true, false, NOW()
                FROM ""Servers"" s;

                INSERT INTO ""ServerRoles"" (""Id"", ""ServerId"", ""Name"", ""Color"", ""Position"", ""Permissions"", ""IsSystemRole"", ""IsHoisted"", ""IsMentionable"", ""CreatedAt"")
                SELECT gen_random_uuid(), s.""Id"", 'Admin', '#f0b232', 1, {AdminDefaults}, true, true, false, NOW()
                FROM ""Servers"" s;

                INSERT INTO ""ServerRoles"" (""Id"", ""ServerId"", ""Name"", ""Color"", ""Position"", ""Permissions"", ""IsSystemRole"", ""IsHoisted"", ""IsMentionable"", ""CreatedAt"")
                SELECT gen_random_uuid(), s.""Id"", 'Member', NULL, 2, {MemberDefaults}, true, false, false, NOW()
                FROM ""Servers"" s;
            ");

            // 3. Add RoleId column (nullable initially for data migration)
            migrationBuilder.AddColumn<Guid>(
                name: "RoleId",
                table: "ServerMembers",
                type: "uuid",
                nullable: true);

            // 4. Migrate existing role data: map old Role int to new RoleId
            //    Old enum: Owner=0, Admin=1, Member=2
            migrationBuilder.Sql(@"
                UPDATE ""ServerMembers"" sm
                SET ""RoleId"" = sr.""Id""
                FROM ""ServerRoles"" sr
                WHERE sr.""ServerId"" = sm.""ServerId""
                  AND sr.""IsSystemRole"" = true
                  AND (
                    (sm.""Role"" = 0 AND sr.""Name"" = 'Owner') OR
                    (sm.""Role"" = 1 AND sr.""Name"" = 'Admin') OR
                    (sm.""Role"" = 2 AND sr.""Name"" = 'Member')
                  );
            ");

            // 5. Drop the old Role column
            migrationBuilder.DropColumn(
                name: "Role",
                table: "ServerMembers");

            // 6. Safety net: assign any remaining NULL RoleId rows to the Member role
            migrationBuilder.Sql(@"
                UPDATE ""ServerMembers"" sm
                SET ""RoleId"" = sr.""Id""
                FROM ""ServerRoles"" sr
                WHERE sm.""RoleId"" IS NULL
                  AND sr.""ServerId"" = sm.""ServerId""
                  AND sr.""IsSystemRole"" = true
                  AND sr.""Name"" = 'Member';
            ");

            // 7. Make RoleId non-nullable now that data is migrated
            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "ServerMembers",
                type: "uuid",
                nullable: false);

            // 8. Add indexes and FK constraint
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-add old Role column
            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "ServerMembers",
                type: "integer",
                nullable: false,
                defaultValue: 2); // Default to Member

            // Migrate back: map role names to enum values
            migrationBuilder.Sql(@"
                UPDATE ""ServerMembers"" sm
                SET ""Role"" = CASE sr.""Name""
                    WHEN 'Owner' THEN 0
                    WHEN 'Admin' THEN 1
                    ELSE 2
                END
                FROM ""ServerRoles"" sr
                WHERE sr.""Id"" = sm.""RoleId"";
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_ServerMembers_ServerRoles_RoleId",
                table: "ServerMembers");

            migrationBuilder.DropIndex(
                name: "IX_ServerMembers_RoleId",
                table: "ServerMembers");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "ServerMembers");

            migrationBuilder.DropTable(
                name: "ServerRoles");
        }
    }
}
