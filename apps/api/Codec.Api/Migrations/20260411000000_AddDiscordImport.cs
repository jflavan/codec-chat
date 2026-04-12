using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscordImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add ImportedAuthorName and ImportedAuthorAvatarUrl to Messages
            migrationBuilder.AddColumn<string>(
                name: "ImportedAuthorName",
                table: "Messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportedAuthorAvatarUrl",
                table: "Messages",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportedDiscordUserId",
                table: "Messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Create DiscordImports table
            migrationBuilder.CreateTable(
                name: "DiscordImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscordGuildId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EncryptedBotToken = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ImportedChannels = table.Column<int>(type: "integer", nullable: false),
                    ImportedMessages = table.Column<int>(type: "integer", nullable: false),
                    ImportedMembers = table.Column<int>(type: "integer", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InitiatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscordImports_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DiscordImports_Users_InitiatedByUserId",
                        column: x => x.InitiatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscordImports_ServerId",
                table: "DiscordImports",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscordImports_InitiatedByUserId",
                table: "DiscordImports",
                column: "InitiatedByUserId");

            // Create DiscordUserMappings table
            migrationBuilder.CreateTable(
                name: "DiscordUserMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscordUserId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DiscordUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DiscordAvatarUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CodecUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordUserMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscordUserMappings_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DiscordUserMappings_Users_CodecUserId",
                        column: x => x.CodecUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscordUserMappings_ServerId_DiscordUserId",
                table: "DiscordUserMappings",
                columns: new[] { "ServerId", "DiscordUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscordUserMappings_CodecUserId",
                table: "DiscordUserMappings",
                column: "CodecUserId");

            // Create DiscordEntityMappings table
            migrationBuilder.CreateTable(
                name: "DiscordEntityMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscordImportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscordEntityId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    CodecEntityId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordEntityMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscordEntityMappings_DiscordImports_DiscordImportId",
                        column: x => x.DiscordImportId,
                        principalTable: "DiscordImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DiscordEntityMappings_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscordEntityMappings_ServerId_DiscordEntityId_EntityType",
                table: "DiscordEntityMappings",
                columns: new[] { "ServerId", "DiscordEntityId", "EntityType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscordEntityMappings_DiscordImportId",
                table: "DiscordEntityMappings",
                column: "DiscordImportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DiscordEntityMappings");
            migrationBuilder.DropTable(name: "DiscordUserMappings");
            migrationBuilder.DropTable(name: "DiscordImports");

            migrationBuilder.DropColumn(name: "ImportedAuthorName", table: "Messages");
            migrationBuilder.DropColumn(name: "ImportedAuthorAvatarUrl", table: "Messages");
            migrationBuilder.DropColumn(name: "ImportedDiscordUserId", table: "Messages");
        }
    }
}
