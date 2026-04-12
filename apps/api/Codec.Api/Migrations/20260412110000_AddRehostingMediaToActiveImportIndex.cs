using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRehostingMediaToActiveImportIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DiscordImports_ServerId_ActiveImport",
                table: "DiscordImports");

            migrationBuilder.CreateIndex(
                name: "IX_DiscordImports_ServerId_ActiveImport",
                table: "DiscordImports",
                column: "ServerId",
                unique: true,
                filter: "\"Status\" IN ('Pending', 'InProgress', 'RehostingMedia')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DiscordImports_ServerId_ActiveImport",
                table: "DiscordImports");

            migrationBuilder.CreateIndex(
                name: "IX_DiscordImports_ServerId_ActiveImport",
                table: "DiscordImports",
                column: "ServerId",
                unique: true,
                filter: "\"Status\" IN ('Pending', 'InProgress')");
        }
    }
}
