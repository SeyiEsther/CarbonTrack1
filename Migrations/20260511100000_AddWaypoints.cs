using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonTrack.Migrations
{
    /// <inheritdoc />
    public partial class AddWaypoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Waypoints",
                table: "Trips",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Waypoints", table: "Trips");
        }
    }
}
