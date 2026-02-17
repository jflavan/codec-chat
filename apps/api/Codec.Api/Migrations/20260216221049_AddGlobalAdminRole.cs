using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalAdminRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsGlobalAdmin",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsGlobalAdmin",
                table: "Users");
        }
    }
}
