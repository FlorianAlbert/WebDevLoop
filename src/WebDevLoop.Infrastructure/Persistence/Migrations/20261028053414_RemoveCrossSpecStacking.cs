using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDevLoop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261028053414_RemoveCrossSpecStacking : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DependencyModeUsed",
            table: "SpecRuns");

        migrationBuilder.DropColumn(
            name: "ModeAtStart",
            table: "SpecDependencies");

        migrationBuilder.DropColumn(
            name: "SpecDependencyMode",
            table: "SettingsProfiles");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DependencyModeUsed",
            table: "SpecRuns",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ModeAtStart",
            table: "SpecDependencies",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "SpecDependencyMode",
            table: "SettingsProfiles",
            type: "TEXT",
            nullable: true);
    }
}
