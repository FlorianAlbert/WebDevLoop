using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDevLoop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261009193106_RemovePatFallbackSetting : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PatFallbackEnabled",
            table: "SettingsProfiles");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "PatFallbackEnabled",
            table: "SettingsProfiles",
            type: "INTEGER",
            nullable: true);
    }
}
