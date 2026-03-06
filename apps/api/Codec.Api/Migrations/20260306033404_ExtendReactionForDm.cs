using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExtendReactionForDm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reactions_MessageId_UserId_Emoji",
                table: "Reactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "MessageId",
                table: "Reactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "DirectMessageId",
                table: "Reactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reactions_DirectMessageId_UserId_Emoji",
                table: "Reactions",
                columns: new[] { "DirectMessageId", "UserId", "Emoji" },
                unique: true,
                filter: "\"DirectMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Reactions_MessageId_UserId_Emoji",
                table: "Reactions",
                columns: new[] { "MessageId", "UserId", "Emoji" },
                unique: true,
                filter: "\"MessageId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reaction_SingleParent",
                table: "Reactions",
                sql: "(\"MessageId\" IS NOT NULL AND \"DirectMessageId\" IS NULL) OR (\"MessageId\" IS NULL AND \"DirectMessageId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Reactions_DirectMessages_DirectMessageId",
                table: "Reactions",
                column: "DirectMessageId",
                principalTable: "DirectMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reactions_DirectMessages_DirectMessageId",
                table: "Reactions");

            migrationBuilder.DropIndex(
                name: "IX_Reactions_DirectMessageId_UserId_Emoji",
                table: "Reactions");

            migrationBuilder.DropIndex(
                name: "IX_Reactions_MessageId_UserId_Emoji",
                table: "Reactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reaction_SingleParent",
                table: "Reactions");

            migrationBuilder.DropColumn(
                name: "DirectMessageId",
                table: "Reactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "MessageId",
                table: "Reactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reactions_MessageId_UserId_Emoji",
                table: "Reactions",
                columns: new[] { "MessageId", "UserId", "Emoji" },
                unique: true);
        }
    }
}
