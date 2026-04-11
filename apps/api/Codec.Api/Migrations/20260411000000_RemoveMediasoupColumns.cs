using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations;

/// <inheritdoc />
public partial class RemoveMediasoupColumns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ParticipantId",
            table: "VoiceStates");

        migrationBuilder.DropColumn(
            name: "ProducerId",
            table: "VoiceStates");

        migrationBuilder.DropColumn(
            name: "VideoProducerId",
            table: "VoiceStates");

        migrationBuilder.DropColumn(
            name: "ScreenProducerId",
            table: "VoiceStates");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ParticipantId",
            table: "VoiceStates",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "ProducerId",
            table: "VoiceStates",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "VideoProducerId",
            table: "VoiceStates",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ScreenProducerId",
            table: "VoiceStates",
            type: "text",
            nullable: true);
    }
}
