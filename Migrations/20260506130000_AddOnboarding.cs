using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarbonTrack.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserType",
                table: "Organisations",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sector",
                table: "Organisations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComplianceFlags",
                table: "Organisations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Employees",
                table: "Organisations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TurnoverGBPm",
                table: "Organisations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OnboardingComplete",
                table: "Organisations",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "UserType",           table: "Organisations");
            migrationBuilder.DropColumn(name: "Sector",             table: "Organisations");
            migrationBuilder.DropColumn(name: "ComplianceFlags",    table: "Organisations");
            migrationBuilder.DropColumn(name: "Employees",          table: "Organisations");
            migrationBuilder.DropColumn(name: "TurnoverGBPm",       table: "Organisations");
            migrationBuilder.DropColumn(name: "OnboardingComplete",  table: "Organisations");
        }
    }
}
