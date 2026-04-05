using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codec.Api.Migrations
{
    /// <inheritdoc />
    public partial class AllowNullableDeletedUserFks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BannedMembers.BannedByUserId: make nullable, change FK from RESTRICT to SET NULL
            migrationBuilder.DropForeignKey(
                name: "FK_BannedMembers_Users_BannedByUserId",
                table: "BannedMembers");

            migrationBuilder.AlterColumn<Guid>(
                name: "BannedByUserId",
                table: "BannedMembers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_BannedMembers_Users_BannedByUserId",
                table: "BannedMembers",
                column: "BannedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // CustomEmojis.UploadedByUserId: make nullable, change FK from RESTRICT to SET NULL
            migrationBuilder.DropForeignKey(
                name: "FK_CustomEmojis_Users_UploadedByUserId",
                table: "CustomEmojis");

            migrationBuilder.AlterColumn<Guid>(
                name: "UploadedByUserId",
                table: "CustomEmojis",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomEmojis_Users_UploadedByUserId",
                table: "CustomEmojis",
                column: "UploadedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Reports.ReporterId: make nullable, change FK from RESTRICT to SET NULL
            migrationBuilder.DropForeignKey(
                name: "FK_Reports_Users_ReporterId",
                table: "Reports");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReporterId",
                table: "Reports",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_Users_ReporterId",
                table: "Reports",
                column: "ReporterId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ServerInvites.CreatedByUserId: make nullable, change FK from CASCADE to SET NULL
            migrationBuilder.DropForeignKey(
                name: "FK_ServerInvites_Users_CreatedByUserId",
                table: "ServerInvites");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "ServerInvites",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_ServerInvites_Users_CreatedByUserId",
                table: "ServerInvites",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // SystemAnnouncements.CreatedByUserId: make nullable, change FK from RESTRICT to SET NULL
            migrationBuilder.DropForeignKey(
                name: "FK_SystemAnnouncements_Users_CreatedByUserId",
                table: "SystemAnnouncements");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "SystemAnnouncements",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_SystemAnnouncements_Users_CreatedByUserId",
                table: "SystemAnnouncements",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Webhooks.CreatedByUserId: make nullable, change FK from RESTRICT to SET NULL
            migrationBuilder.DropForeignKey(
                name: "FK_Webhooks_Users_CreatedByUserId",
                table: "Webhooks");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "Webhooks",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_Webhooks_Users_CreatedByUserId",
                table: "Webhooks",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert Webhooks.CreatedByUserId
            migrationBuilder.DropForeignKey(
                name: "FK_Webhooks_Users_CreatedByUserId",
                table: "Webhooks");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "Webhooks",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Webhooks_Users_CreatedByUserId",
                table: "Webhooks",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Revert SystemAnnouncements.CreatedByUserId
            migrationBuilder.DropForeignKey(
                name: "FK_SystemAnnouncements_Users_CreatedByUserId",
                table: "SystemAnnouncements");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "SystemAnnouncements",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SystemAnnouncements_Users_CreatedByUserId",
                table: "SystemAnnouncements",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Revert ServerInvites.CreatedByUserId
            migrationBuilder.DropForeignKey(
                name: "FK_ServerInvites_Users_CreatedByUserId",
                table: "ServerInvites");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "ServerInvites",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ServerInvites_Users_CreatedByUserId",
                table: "ServerInvites",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Revert Reports.ReporterId
            migrationBuilder.DropForeignKey(
                name: "FK_Reports_Users_ReporterId",
                table: "Reports");

            migrationBuilder.AlterColumn<Guid>(
                name: "ReporterId",
                table: "Reports",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_Users_ReporterId",
                table: "Reports",
                column: "ReporterId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Revert CustomEmojis.UploadedByUserId
            migrationBuilder.DropForeignKey(
                name: "FK_CustomEmojis_Users_UploadedByUserId",
                table: "CustomEmojis");

            migrationBuilder.AlterColumn<Guid>(
                name: "UploadedByUserId",
                table: "CustomEmojis",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomEmojis_Users_UploadedByUserId",
                table: "CustomEmojis",
                column: "UploadedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Revert BannedMembers.BannedByUserId
            migrationBuilder.DropForeignKey(
                name: "FK_BannedMembers_Users_BannedByUserId",
                table: "BannedMembers");

            migrationBuilder.AlterColumn<Guid>(
                name: "BannedByUserId",
                table: "BannedMembers",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BannedMembers_Users_BannedByUserId",
                table: "BannedMembers",
                column: "BannedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
