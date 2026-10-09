using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDevLoop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261009153925_RunNeedsAttentionPhase : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NeedsAttentionFrom",
            table: "TicketRuns",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NeedsAttentionFrom",
            table: "SpecRuns",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "NeedsAttentionFrom",
            table: "TicketRuns");

        migrationBuilder.DropColumn(
            name: "NeedsAttentionFrom",
            table: "SpecRuns");
    }
}
