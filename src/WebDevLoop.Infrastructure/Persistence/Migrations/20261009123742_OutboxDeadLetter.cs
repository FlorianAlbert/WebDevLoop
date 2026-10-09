using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDevLoop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261009123742_OutboxDeadLetter : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OutboxMessages_Pending",
            table: "OutboxMessages");

        migrationBuilder.AddColumn<long>(
            name: "DeadLetteredAt",
            table: "OutboxMessages",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_Pending",
            table: "OutboxMessages",
            column: "DispatchedAt",
            filter: "\"DispatchedAt\" IS NULL AND \"DeadLetteredAt\" IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OutboxMessages_Pending",
            table: "OutboxMessages");

        migrationBuilder.DropColumn(
            name: "DeadLetteredAt",
            table: "OutboxMessages");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_Pending",
            table: "OutboxMessages",
            column: "DispatchedAt",
            filter: "\"DispatchedAt\" IS NULL");
    }
}
