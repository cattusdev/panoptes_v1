using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panoptes_v1.Migrations
{
    /// <inheritdoc />
    public partial class AddSequencesAndTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "AppSettings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedById",
                table: "AppSettings",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedById",
                table: "AppSettings",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            // Οι υπάρχουσες ρυθμίσεις: η πρώτη γνωστή στιγμή τους είναι η τελευταία αλλαγή
            migrationBuilder.Sql("""UPDATE "AppSettings" SET "CreatedAt" = "UpdatedAt";""");

            migrationBuilder.CreateTable(
                name: "Sequences",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NextValue = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sequences", x => x.Name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sequences");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "AppSettings");
        }
    }
}
