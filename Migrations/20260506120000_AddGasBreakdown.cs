using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonTrack.Migrations
{
    /// <inheritdoc />
    public partial class AddGasBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "KgCO2",
                table: "Trips",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "KgCH4",
                table: "Trips",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "KgN2O",
                table: "Trips",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "KgCO2", table: "Trips");
            migrationBuilder.DropColumn(name: "KgCH4", table: "Trips");
            migrationBuilder.DropColumn(name: "KgN2O", table: "Trips");
        }
    }
}
