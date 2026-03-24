using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoScreenSharingToVoiceState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsScreenSharing",
                table: "VoiceStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVideoEnabled",
                table: "VoiceStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ScreenProducerId",
                table: "VoiceStates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoProducerId",
                table: "VoiceStates",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsScreenSharing",
                table: "VoiceStates");

            migrationBuilder.DropColumn(
                name: "IsVideoEnabled",
                table: "VoiceStates");

            migrationBuilder.DropColumn(
                name: "ScreenProducerId",
                table: "VoiceStates");

            migrationBuilder.DropColumn(
                name: "VideoProducerId",
                table: "VoiceStates");
        }
    }
}
