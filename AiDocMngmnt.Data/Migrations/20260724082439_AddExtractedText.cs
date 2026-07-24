using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiDocMngmnt.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExtractedText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedText",
                table: "Documents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractedText",
                table: "Documents");
        }
    }
}
