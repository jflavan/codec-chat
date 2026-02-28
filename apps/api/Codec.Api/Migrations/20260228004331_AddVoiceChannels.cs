using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Channels",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "VoiceStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsMuted = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeafened = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectionId = table.Column<string>(type: "text", nullable: false),
                    ParticipantId = table.Column<string>(type: "text", nullable: false),
                    ProducerId = table.Column<string>(type: "text", nullable: true),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoiceStates_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VoiceStates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceStates_ChannelId",
                table: "VoiceStates",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceStates_ConnectionId",
                table: "VoiceStates",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceStates_UserId",
                table: "VoiceStates",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoiceStates");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Channels");
        }
    }
}
