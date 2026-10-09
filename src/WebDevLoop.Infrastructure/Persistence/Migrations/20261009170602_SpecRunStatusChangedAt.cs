using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDevLoop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261009170602_SpecRunStatusChangedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "StatusChangedAt",
            table: "SpecRuns",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0L);

        // Existing runs: the latest transition recorded so far (timestamps are UTC ticks).
        migrationBuilder.Sql(
            "UPDATE \"SpecRuns\" SET \"StatusChangedAt\" = MAX(\"CreatedAt\", COALESCE(\"StartedAt\", 0), COALESCE(\"ReadyAt\", 0), COALESCE(\"CompletedAt\", 0))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "StatusChangedAt",
            table: "SpecRuns");
    }
}
