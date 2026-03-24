using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFileAttachmentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
