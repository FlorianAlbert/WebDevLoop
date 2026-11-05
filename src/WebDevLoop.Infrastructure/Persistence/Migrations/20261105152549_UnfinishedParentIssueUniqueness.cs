using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDevLoop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261105152549_UnfinishedParentIssueUniqueness : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ClaimsParentIssue",
            table: "SpecRuns",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);

        // Earlier versions could store several unfinished runs of one parent issue. Aborting them here would bypass the abort
        // lifecycle, so they all keep running and stay abortable; only the most advanced one keeps the unique claim.
        migrationBuilder.Sql(
            """
            UPDATE "SpecRuns"
            SET "ClaimsParentIssue" = 0
            WHERE "Id" IN (
                SELECT "Id" FROM (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "RepositoryId", "ParentIssue_Number"
                        ORDER BY CASE WHEN "Status" = 'Queued' THEN 1 ELSE 0 END, "QueuePosition", "Id") AS "Rank"
                    FROM "SpecRuns"
                    WHERE "Status" NOT IN ('Completed', 'Aborted'))
                WHERE "Rank" > 1)
            """);

        migrationBuilder.CreateIndex(
            name: "UX_SpecRuns_UnfinishedParentIssue",
            table: "SpecRuns",
            columns: new[] { "RepositoryId", "ParentIssue_Number" },
            unique: true,
            filter: "\"Status\" NOT IN ('Completed', 'Aborted') AND \"ClaimsParentIssue\" = 1");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_SpecRuns_UnfinishedParentIssue",
            table: "SpecRuns");

        migrationBuilder.DropColumn(
            name: "ClaimsParentIssue",
            table: "SpecRuns");
    }
}
