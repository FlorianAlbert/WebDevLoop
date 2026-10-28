using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDevLoop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261028070440_CurrentWorkDecisionReason : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "WorkDecisionReason",
            table: "SpecRuns",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "WorkDecisionReason",
            table: "SpecRuns");
    }
}
