using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBannedMembers : Migration
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

            migrationBuilder.AddColumn<string>(
                name: "StatusEmoji",
                table: "Users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusText",
                table: "Users",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileContentType",
                table: "Messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "Messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "Messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileUrl",
                table: "Messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileContentType",
                table: "DirectMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "DirectMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "DirectMessages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileUrl",
                table: "DirectMessages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BannedMembers",
                columns: table => new
                {
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BannedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    BannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BannedMembers", x => new { x.ServerId, x.UserId });
                    table.ForeignKey(
                        name: "FK_BannedMembers_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BannedMembers_Users_BannedByUserId",
                        column: x => x.BannedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BannedMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_BannedMembers_BannedByUserId",
                table: "BannedMembers",
                column: "BannedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BannedMembers_UserId",
                table: "BannedMembers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BannedMembers");

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

            migrationBuilder.DropColumn(
                name: "StatusEmoji",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StatusText",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FileContentType",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "FileUrl",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "FileContentType",
                table: "DirectMessages");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "DirectMessages");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "DirectMessages");

            migrationBuilder.DropColumn(
                name: "FileUrl",
                table: "DirectMessages");
        }
    }
}
