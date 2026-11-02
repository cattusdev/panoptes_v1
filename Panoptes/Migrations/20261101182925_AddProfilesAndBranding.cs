using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panoptes.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilesAndBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrandingSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    AppName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LogoMediaId = table.Column<Guid>(type: "uuid", nullable: true),
                    FaviconMediaId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandingSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrandingSettings_MediaItems_FaviconMediaId",
                        column: x => x.FaviconMediaId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BrandingSettings_MediaItems_LogoMediaId",
                        column: x => x.LogoMediaId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AvatarMediaId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserProfiles_MediaItems_AvatarMediaId",
                        column: x => x.AvatarMediaId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrandingSettings_FaviconMediaId",
                table: "BrandingSettings",
                column: "FaviconMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_BrandingSettings_LogoMediaId",
                table: "BrandingSettings",
                column: "LogoMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_AvatarMediaId",
                table: "UserProfiles",
                column: "AvatarMediaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrandingSettings");

            migrationBuilder.DropTable(
                name: "UserProfiles");
        }
    }
}
