using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubAndDiscordOAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiscordSubject",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitHubSubject",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_DiscordSubject",
                table: "Users",
                column: "DiscordSubject",
                unique: true,
                filter: "\"DiscordSubject\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_GitHubSubject",
                table: "Users",
                column: "GitHubSubject",
                unique: true,
                filter: "\"GitHubSubject\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_DiscordSubject",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_GitHubSubject",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DiscordSubject",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GitHubSubject",
                table: "Users");
        }
    }
}
