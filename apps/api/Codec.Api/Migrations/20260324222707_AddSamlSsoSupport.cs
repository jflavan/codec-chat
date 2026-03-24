using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSamlSsoSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SamlIdentityProviderId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SamlNameId",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SamlIdentityProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SingleSignOnUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CertificatePem = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowJitProvisioning = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SamlIdentityProviders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_SamlIdentityProviderId",
                table: "Users",
                column: "SamlIdentityProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_SamlNameId_SamlIdentityProviderId",
                table: "Users",
                columns: new[] { "SamlNameId", "SamlIdentityProviderId" },
                unique: true,
                filter: "\"SamlNameId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SamlIdentityProviders_EntityId",
                table: "SamlIdentityProviders",
                column: "EntityId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_SamlIdentityProviders_SamlIdentityProviderId",
                table: "Users",
                column: "SamlIdentityProviderId",
                principalTable: "SamlIdentityProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_SamlIdentityProviders_SamlIdentityProviderId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "SamlIdentityProviders");

            migrationBuilder.DropIndex(
                name: "IX_Users_SamlIdentityProviderId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_SamlNameId_SamlIdentityProviderId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SamlIdentityProviderId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SamlNameId",
                table: "Users");
        }
    }
}
