using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDevLoop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261010150022_TroubleshooterSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "TroubleshooterEnabled",
            table: "SettingsProfiles",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TroubleshooterMaxAttempts",
            table: "SettingsProfiles",
            type: "INTEGER",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TroubleshooterEnabled",
            table: "SettingsProfiles");

        migrationBuilder.DropColumn(
            name: "TroubleshooterMaxAttempts",
            table: "SettingsProfiles");
    }
}
