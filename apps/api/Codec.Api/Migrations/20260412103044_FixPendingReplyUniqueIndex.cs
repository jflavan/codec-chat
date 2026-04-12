using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixPendingReplyUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DiscordEntityMappings_ServerId_DiscordEntityId_EntityType",
                table: "DiscordEntityMappings");

            migrationBuilder.CreateIndex(
                name: "IX_DiscordEntityMappings_ServerId_DiscordEntityId_EntityType",
                table: "DiscordEntityMappings",
                columns: new[] { "ServerId", "DiscordEntityId", "EntityType" },
                unique: true,
                filter: "\"EntityType\" != 'PendingReply'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DiscordEntityMappings_ServerId_DiscordEntityId_EntityType",
                table: "DiscordEntityMappings");

            migrationBuilder.CreateIndex(
                name: "IX_DiscordEntityMappings_ServerId_DiscordEntityId_EntityType",
                table: "DiscordEntityMappings",
                columns: new[] { "ServerId", "DiscordEntityId", "EntityType" },
                unique: true);
        }
    }
}
