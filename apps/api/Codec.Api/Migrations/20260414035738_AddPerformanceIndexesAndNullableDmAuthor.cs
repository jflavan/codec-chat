using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexesAndNullableDmAuthor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DirectMessages_Users_AuthorUserId",
                table: "DirectMessages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ChannelId",
                table: "Messages");

            migrationBuilder.AlterColumn<Guid>(
                name: "AuthorUserId",
                table: "DirectMessages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChannelId_CreatedAt",
                table: "Messages",
                columns: new[] { "ChannelId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_DirectMessages_DmChannelId_CreatedAt",
                table: "DirectMessages",
                columns: new[] { "DmChannelId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.AddForeignKey(
                name: "FK_DirectMessages_Users_AuthorUserId",
                table: "DirectMessages",
                column: "AuthorUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DirectMessages_Users_AuthorUserId",
                table: "DirectMessages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ChannelId_CreatedAt",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_DirectMessages_DmChannelId_CreatedAt",
                table: "DirectMessages");

            migrationBuilder.AlterColumn<Guid>(
                name: "AuthorUserId",
                table: "DirectMessages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ChannelId",
                table: "Messages",
                column: "ChannelId");

            migrationBuilder.AddForeignKey(
                name: "FK_DirectMessages_Users_AuthorUserId",
                table: "DirectMessages",
                column: "AuthorUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
