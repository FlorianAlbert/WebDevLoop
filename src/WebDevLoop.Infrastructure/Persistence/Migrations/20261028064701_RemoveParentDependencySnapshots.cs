using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDevLoop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261028064701_RemoveParentDependencySnapshots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SpecDependencies");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SpecDependencies",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                BlockedSpecRunId = table.Column<string>(type: "TEXT", nullable: false),
                BlockingSpecRunId = table.Column<string>(type: "TEXT", nullable: true),
                Source = table.Column<string>(type: "TEXT", nullable: false),
                ExternalBlockingIssue_DatabaseId = table.Column<long>(type: "INTEGER", nullable: true),
                ExternalBlockingIssue_NodeId = table.Column<string>(type: "TEXT", nullable: true),
                ExternalBlockingIssue_Number = table.Column<int>(type: "INTEGER", nullable: true),
                ExternalBlockingIssue_Owner = table.Column<string>(type: "TEXT", nullable: true),
                ExternalBlockingIssue_Repo = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SpecDependencies", x => x.Id);
                table.ForeignKey(
                    name: "FK_SpecDependencies_SpecRuns_BlockedSpecRunId",
                    column: x => x.BlockedSpecRunId,
                    principalTable: "SpecRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SpecDependencies_SpecRuns_BlockingSpecRunId",
                    column: x => x.BlockingSpecRunId,
                    principalTable: "SpecRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SpecDependencies_BlockingSpecRunId",
            table: "SpecDependencies",
            column: "BlockingSpecRunId");

        migrationBuilder.CreateIndex(
            name: "UX_SpecDependencies_Blocked_Blocking",
            table: "SpecDependencies",
            columns: new[] { "BlockedSpecRunId", "BlockingSpecRunId" },
            unique: true,
            filter: "\"BlockingSpecRunId\" IS NOT NULL");
    }
}
