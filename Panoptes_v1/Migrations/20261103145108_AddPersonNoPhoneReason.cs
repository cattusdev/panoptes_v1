using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panoptes_v1.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonNoPhoneReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NoPhoneReason",
                table: "People",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoPhoneReason",
                table: "People");
        }
    }
}
