using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueActiveCallIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VoiceCalls_DmChannelId",
                table: "VoiceCalls");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCalls_DmChannelId_ActiveOrRinging",
                table: "VoiceCalls",
                column: "DmChannelId",
                unique: true,
                filter: "\"Status\" IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VoiceCalls_DmChannelId_ActiveOrRinging",
                table: "VoiceCalls");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceCalls_DmChannelId",
                table: "VoiceCalls",
                column: "DmChannelId");
        }
    }
}
