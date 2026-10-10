using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDevLoop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261010125622_AttentionReasons : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Attention",
            table: "TicketRuns",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Attention",
            table: "StepRuns",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Attention",
            table: "SpecRuns",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Attention",
            table: "TicketRuns");

        migrationBuilder.DropColumn(
            name: "Attention",
            table: "StepRuns");

        migrationBuilder.DropColumn(
            name: "Attention",
            table: "SpecRuns");
    }
}
