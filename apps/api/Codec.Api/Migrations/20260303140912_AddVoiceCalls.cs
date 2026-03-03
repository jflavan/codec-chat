using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DmChannelId",
                table: "VoiceStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MessageType",
                table: "DirectMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "VoiceCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DmChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CallerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EndReason = table.Column<int>(type: "integer", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AnsweredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoiceCalls_DmChannels_DmChannelId",
                        column: x => x.DmChannelId,
                        principalTable: "DmChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VoiceCalls_Users_CallerUserId",
                        column: x => x.CallerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoiceCalls_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceStates_DmChannelId",
                table: "VoiceStates",
                column: "DmChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCalls_CallerUserId",
                table: "VoiceCalls",
                column: "CallerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCalls_DmChannelId",
                table: "VoiceCalls",
                column: "DmChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCalls_RecipientUserId",
                table: "VoiceCalls",
                column: "RecipientUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_VoiceStates_DmChannels_DmChannelId",
                table: "VoiceStates",
                column: "DmChannelId",
                principalTable: "DmChannels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VoiceStates_DmChannels_DmChannelId",
                table: "VoiceStates");

            migrationBuilder.DropTable(
                name: "VoiceCalls");

            migrationBuilder.DropIndex(
                name: "IX_VoiceStates_DmChannelId",
                table: "VoiceStates");

            migrationBuilder.DropColumn(
                name: "DmChannelId",
                table: "VoiceStates");

            migrationBuilder.DropColumn(
                name: "MessageType",
                table: "DirectMessages");
        }
    }
}
