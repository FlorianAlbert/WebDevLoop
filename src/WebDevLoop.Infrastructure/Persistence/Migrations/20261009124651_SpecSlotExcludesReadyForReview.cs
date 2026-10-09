using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDevLoop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261009124651_SpecSlotExcludesReadyForReview : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE \"SpecRuns\" SET \"MaxActiveSpecsSlot\" = NULL WHERE \"Status\" IN ('ReadyForReview', 'AwaitingMerge')");

        migrationBuilder.DropIndex(
            name: "UX_SpecRuns_ActiveSlotPerRepository",
            table: "SpecRuns");

        migrationBuilder.CreateIndex(
            name: "UX_SpecRuns_ActiveSlotPerRepository",
            table: "SpecRuns",
            columns: new[] { "RepositoryId", "MaxActiveSpecsSlot" },
            unique: true,
            filter: "\"Status\" IN ('Preparing', 'Running', 'ParentReviewing', 'Testing')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_SpecRuns_ActiveSlotPerRepository",
            table: "SpecRuns");

        migrationBuilder.CreateIndex(
            name: "UX_SpecRuns_ActiveSlotPerRepository",
            table: "SpecRuns",
            columns: new[] { "RepositoryId", "MaxActiveSpecsSlot" },
            unique: true,
            filter: "\"Status\" IN ('Preparing', 'Running', 'ParentReviewing', 'Testing', 'ReadyForReview', 'AwaitingMerge')");
    }
}
