using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebDevLoop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261009114400_InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OutboxMessages",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Type = table.Column<string>(type: "TEXT", nullable: false),
                PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                DispatchedAt = table.Column<long>(type: "INTEGER", nullable: true),
                Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                LastError = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OutboxMessages", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Repositories",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Owner = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                Name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                DefaultBaseBranch = table.Column<string>(type: "TEXT", nullable: false),
                CloneUrl = table.Column<string>(type: "TEXT", nullable: false),
                LocalPath = table.Column<string>(type: "TEXT", nullable: false),
                IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                Version = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Repositories", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "SettingsProfiles",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                RepositoryId = table.Column<int>(type: "INTEGER", nullable: true),
                WorkspaceRootDirectory = table.Column<string>(type: "TEXT", nullable: true),
                CopilotBaseDirectory = table.Column<string>(type: "TEXT", nullable: true),
                BaseBranch = table.Column<string>(type: "TEXT", nullable: true),
                MaxActiveSpecsPerRepo = table.Column<int>(type: "INTEGER", nullable: true),
                SpecDependencyMode = table.Column<string>(type: "TEXT", nullable: true),
                MaxConcurrentImplementersGlobal = table.Column<int>(type: "INTEGER", nullable: true),
                MaxConcurrentImplementersPerRepo = table.Column<int>(type: "INTEGER", nullable: true),
                MaxReviewIterations = table.Column<int>(type: "INTEGER", nullable: true),
                MaxRetries = table.Column<int>(type: "INTEGER", nullable: true),
                ParentReviewCycleLimit = table.Column<int>(type: "INTEGER", nullable: true),
                TesterCycleLimit = table.Column<int>(type: "INTEGER", nullable: true),
                TesterRunInstructions = table.Column<string>(type: "TEXT", nullable: true),
                PatFallbackEnabled = table.Column<bool>(type: "INTEGER", nullable: true),
                ScopeKey = table.Column<int>(type: "INTEGER", nullable: false, computedColumnSql: "COALESCE(\"RepositoryId\", 0)", stored: true),
                RolesJson = table.Column<string>(type: "TEXT", nullable: false),
                TestPortRange_End = table.Column<int>(type: "INTEGER", nullable: true),
                TestPortRange_Start = table.Column<int>(type: "INTEGER", nullable: true),
                Version = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SettingsProfiles", x => x.Id);
                table.ForeignKey(
                    name: "FK_SettingsProfiles_Repositories_RepositoryId",
                    column: x => x.RepositoryId,
                    principalTable: "Repositories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SpecRuns",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                RepositoryId = table.Column<int>(type: "INTEGER", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                BodySnapshot = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                QueuePosition = table.Column<int>(type: "INTEGER", nullable: false),
                BaseBranch = table.Column<string>(type: "TEXT", nullable: true),
                IntegrationBranch = table.Column<string>(type: "TEXT", nullable: false),
                IntegrationBaseSha = table.Column<string>(type: "TEXT", nullable: true),
                IntegrationTipSha = table.Column<string>(type: "TEXT", nullable: true),
                DependencyModeUsed = table.Column<string>(type: "TEXT", nullable: true),
                MaxActiveSpecsSlot = table.Column<int>(type: "INTEGER", nullable: true),
                ReviewCycle = table.Column<int>(type: "INTEGER", nullable: false),
                TestCycle = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                ReadyAt = table.Column<long>(type: "INTEGER", nullable: true),
                CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                ParentIssue_DatabaseId = table.Column<long>(type: "INTEGER", nullable: true),
                ParentIssue_NodeId = table.Column<string>(type: "TEXT", nullable: true),
                ParentIssue_Number = table.Column<int>(type: "INTEGER", nullable: false),
                ParentIssue_Owner = table.Column<string>(type: "TEXT", nullable: false),
                ParentIssue_Repo = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SpecRuns", x => x.Id);
                table.ForeignKey(
                    name: "FK_SpecRuns_Repositories_RepositoryId",
                    column: x => x.RepositoryId,
                    principalTable: "Repositories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SpecDependencies",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                BlockedSpecRunId = table.Column<string>(type: "TEXT", nullable: false),
                BlockingSpecRunId = table.Column<string>(type: "TEXT", nullable: true),
                Source = table.Column<string>(type: "TEXT", nullable: false),
                ModeAtStart = table.Column<string>(type: "TEXT", nullable: false),
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

        migrationBuilder.CreateTable(
            name: "TestLeases",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SpecRunId = table.Column<string>(type: "TEXT", nullable: false),
                Port = table.Column<int>(type: "INTEGER", nullable: false),
                WorkspacePath = table.Column<string>(type: "TEXT", nullable: false),
                ProcessId = table.Column<int>(type: "INTEGER", nullable: true),
                AcquiredAt = table.Column<long>(type: "INTEGER", nullable: false),
                ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                ReleasedAt = table.Column<long>(type: "INTEGER", nullable: true),
                Version = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TestLeases", x => x.Id);
                table.ForeignKey(
                    name: "FK_TestLeases_SpecRuns_SpecRunId",
                    column: x => x.SpecRunId,
                    principalTable: "SpecRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TicketRuns",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                SpecRunId = table.Column<string>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                BodySnapshot = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                ReviewIteration = table.Column<int>(type: "INTEGER", nullable: false),
                BranchName = table.Column<string>(type: "TEXT", nullable: false),
                WorktreePath = table.Column<string>(type: "TEXT", nullable: true),
                LastImplementedSha = table.Column<string>(type: "TEXT", nullable: true),
                IntegratedCommitSha = table.Column<string>(type: "TEXT", nullable: true),
                PullRequestNumber = table.Column<int>(type: "INTEGER", nullable: true),
                StackPosition = table.Column<int>(type: "INTEGER", nullable: true),
                FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                Issue_DatabaseId = table.Column<long>(type: "INTEGER", nullable: true),
                Issue_NodeId = table.Column<string>(type: "TEXT", nullable: true),
                Issue_Number = table.Column<int>(type: "INTEGER", nullable: false),
                Issue_Owner = table.Column<string>(type: "TEXT", nullable: false),
                Issue_Repo = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TicketRuns", x => x.Id);
                table.ForeignKey(
                    name: "FK_TicketRuns_SpecRuns_SpecRunId",
                    column: x => x.SpecRunId,
                    principalTable: "SpecRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "IntegrationSagas",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SpecRunId = table.Column<string>(type: "TEXT", nullable: false),
                TicketRunId = table.Column<string>(type: "TEXT", nullable: false),
                ExpectedPriorIntegrationSha = table.Column<string>(type: "TEXT", nullable: true),
                SquashCommitSha = table.Column<string>(type: "TEXT", nullable: true),
                StackBranchName = table.Column<string>(type: "TEXT", nullable: false),
                PullRequestNumber = table.Column<int>(type: "INTEGER", nullable: true),
                StackNumber = table.Column<int>(type: "INTEGER", nullable: true),
                Checkpoint = table.Column<string>(type: "TEXT", nullable: false),
                ExternalIdempotencyKey = table.Column<string>(type: "TEXT", nullable: false),
                LastError = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                Version = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntegrationSagas", x => x.Id);
                table.ForeignKey(
                    name: "FK_IntegrationSagas_SpecRuns_SpecRunId",
                    column: x => x.SpecRunId,
                    principalTable: "SpecRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_IntegrationSagas_TicketRuns_TicketRunId",
                    column: x => x.TicketRunId,
                    principalTable: "TicketRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PullStackLayers",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SpecRunId = table.Column<string>(type: "TEXT", nullable: false),
                TicketRunId = table.Column<string>(type: "TEXT", nullable: false),
                BranchName = table.Column<string>(type: "TEXT", nullable: false),
                CommitSha = table.Column<string>(type: "TEXT", nullable: false),
                PullRequestNumber = table.Column<int>(type: "INTEGER", nullable: false),
                BaseBranch = table.Column<string>(type: "TEXT", nullable: false),
                StackNumber = table.Column<int>(type: "INTEGER", nullable: true),
                Position = table.Column<int>(type: "INTEGER", nullable: false),
                IsDraft = table.Column<bool>(type: "INTEGER", nullable: false),
                VerifiedDiffSha = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PullStackLayers", x => x.Id);
                table.ForeignKey(
                    name: "FK_PullStackLayers_SpecRuns_SpecRunId",
                    column: x => x.SpecRunId,
                    principalTable: "SpecRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PullStackLayers_TicketRuns_TicketRunId",
                    column: x => x.TicketRunId,
                    principalTable: "TicketRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "RunEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SpecRunId = table.Column<string>(type: "TEXT", nullable: false),
                TicketRunId = table.Column<string>(type: "TEXT", nullable: true),
                Type = table.Column<string>(type: "TEXT", nullable: false),
                PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RunEvents", x => x.Id);
                table.ForeignKey(
                    name: "FK_RunEvents_SpecRuns_SpecRunId",
                    column: x => x.SpecRunId,
                    principalTable: "SpecRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_RunEvents_TicketRuns_TicketRunId",
                    column: x => x.TicketRunId,
                    principalTable: "TicketRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "StepRuns",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                SpecRunId = table.Column<string>(type: "TEXT", nullable: false),
                TicketRunId = table.Column<string>(type: "TEXT", nullable: true),
                Kind = table.Column<string>(type: "TEXT", nullable: false),
                AgentRole = table.Column<string>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                CopilotSessionId = table.Column<string>(type: "TEXT", nullable: true),
                WorktreePath = table.Column<string>(type: "TEXT", nullable: true),
                BranchName = table.Column<string>(type: "TEXT", nullable: true),
                StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                TimeoutAt = table.Column<long>(type: "INTEGER", nullable: true),
                InputPromptHash = table.Column<string>(type: "TEXT", nullable: false),
                StructuredResultJson = table.Column<string>(type: "TEXT", nullable: true),
                FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                Version = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StepRuns", x => x.Id);
                table.ForeignKey(
                    name: "FK_StepRuns_SpecRuns_SpecRunId",
                    column: x => x.SpecRunId,
                    principalTable: "SpecRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StepRuns_TicketRuns_TicketRunId",
                    column: x => x.TicketRunId,
                    principalTable: "TicketRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TicketDependencies",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SpecRunId = table.Column<string>(type: "TEXT", nullable: false),
                BlockedTicketRunId = table.Column<string>(type: "TEXT", nullable: false),
                BlockingTicketRunId = table.Column<string>(type: "TEXT", nullable: false),
                Source = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TicketDependencies", x => x.Id);
                table.ForeignKey(
                    name: "FK_TicketDependencies_SpecRuns_SpecRunId",
                    column: x => x.SpecRunId,
                    principalTable: "SpecRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TicketDependencies_TicketRuns_BlockedTicketRunId",
                    column: x => x.BlockedTicketRunId,
                    principalTable: "TicketRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TicketDependencies_TicketRuns_BlockingTicketRunId",
                    column: x => x.BlockingTicketRunId,
                    principalTable: "TicketRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "FindingIssuances",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                SpecRunId = table.Column<string>(type: "TEXT", nullable: false),
                SourceStepRunId = table.Column<string>(type: "TEXT", nullable: false),
                Axis = table.Column<string>(type: "TEXT", nullable: false),
                Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                IssueNumber = table.Column<int>(type: "INTEGER", nullable: true),
                IssueDatabaseId = table.Column<long>(type: "INTEGER", nullable: true),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FindingIssuances", x => x.Id);
                table.ForeignKey(
                    name: "FK_FindingIssuances_SpecRuns_SpecRunId",
                    column: x => x.SpecRunId,
                    principalTable: "SpecRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_FindingIssuances_StepRuns_SourceStepRunId",
                    column: x => x.SourceStepRunId,
                    principalTable: "StepRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FindingIssuances_SourceStep",
            table: "FindingIssuances",
            column: "SourceStepRunId");

        migrationBuilder.CreateIndex(
            name: "UX_FindingIssuances_SpecRun_Fingerprint",
            table: "FindingIssuances",
            columns: new[] { "SpecRunId", "Fingerprint" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IntegrationSagas_SpecRunId",
            table: "IntegrationSagas",
            column: "SpecRunId");

        migrationBuilder.CreateIndex(
            name: "UX_IntegrationSagas_IncompletePerTicket",
            table: "IntegrationSagas",
            column: "TicketRunId",
            unique: true,
            filter: "\"Checkpoint\" <> 'Completed'");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_Pending",
            table: "OutboxMessages",
            column: "DispatchedAt",
            filter: "\"DispatchedAt\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_PullStackLayers_TicketRunId",
            table: "PullStackLayers",
            column: "TicketRunId");

        migrationBuilder.CreateIndex(
            name: "UX_PullStackLayers_SpecRun_Position",
            table: "PullStackLayers",
            columns: new[] { "SpecRunId", "Position" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_Repositories_Owner_Name",
            table: "Repositories",
            columns: new[] { "Owner", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RunEvents_SpecRun",
            table: "RunEvents",
            column: "SpecRunId");

        migrationBuilder.CreateIndex(
            name: "IX_RunEvents_TicketRunId",
            table: "RunEvents",
            column: "TicketRunId");

        migrationBuilder.CreateIndex(
            name: "IX_SettingsProfiles_RepositoryId",
            table: "SettingsProfiles",
            column: "RepositoryId");

        migrationBuilder.CreateIndex(
            name: "UX_SettingsProfiles_Scope",
            table: "SettingsProfiles",
            column: "ScopeKey",
            unique: true);

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

        migrationBuilder.CreateIndex(
            name: "IX_SpecRuns_Repository_QueuePosition",
            table: "SpecRuns",
            columns: new[] { "RepositoryId", "QueuePosition" });

        migrationBuilder.CreateIndex(
            name: "IX_SpecRuns_Status",
            table: "SpecRuns",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "UX_SpecRuns_ActiveSlotPerRepository",
            table: "SpecRuns",
            columns: new[] { "RepositoryId", "MaxActiveSpecsSlot" },
            unique: true,
            filter: "\"Status\" IN ('Preparing', 'Running', 'ParentReviewing', 'Testing', 'ReadyForReview', 'AwaitingMerge')");

        migrationBuilder.CreateIndex(
            name: "IX_StepRuns_SpecRun",
            table: "StepRuns",
            column: "SpecRunId");

        migrationBuilder.CreateIndex(
            name: "IX_StepRuns_Status",
            table: "StepRuns",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "UX_StepRuns_ActiveAppOwnedKindPerRun",
            table: "StepRuns",
            columns: new[] { "SpecRunId", "Kind" },
            unique: true,
            filter: "\"AgentRole\" IS NULL AND \"Status\" IN ('Pending', 'Running')");

        migrationBuilder.CreateIndex(
            name: "UX_StepRuns_ActiveImplementOrFixPerTicket",
            table: "StepRuns",
            column: "TicketRunId",
            unique: true,
            filter: "\"Kind\" IN ('Implement', 'Fix') AND \"Status\" IN ('Pending', 'Running')");

        migrationBuilder.CreateIndex(
            name: "UX_TestLeases_ActivePerRun",
            table: "TestLeases",
            column: "SpecRunId",
            unique: true,
            filter: "\"ReleasedAt\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "UX_TestLeases_ActivePort",
            table: "TestLeases",
            column: "Port",
            unique: true,
            filter: "\"ReleasedAt\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_TicketDependencies_BlockingTicketRunId",
            table: "TicketDependencies",
            column: "BlockingTicketRunId");

        migrationBuilder.CreateIndex(
            name: "IX_TicketDependencies_SpecRun",
            table: "TicketDependencies",
            column: "SpecRunId");

        migrationBuilder.CreateIndex(
            name: "UX_TicketDependencies_Blocked_Blocking",
            table: "TicketDependencies",
            columns: new[] { "BlockedTicketRunId", "BlockingTicketRunId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TicketRuns_SpecRun_Status",
            table: "TicketRuns",
            columns: new[] { "SpecRunId", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "FindingIssuances");

        migrationBuilder.DropTable(
            name: "IntegrationSagas");

        migrationBuilder.DropTable(
            name: "OutboxMessages");

        migrationBuilder.DropTable(
            name: "PullStackLayers");

        migrationBuilder.DropTable(
            name: "RunEvents");

        migrationBuilder.DropTable(
            name: "SettingsProfiles");

        migrationBuilder.DropTable(
            name: "SpecDependencies");

        migrationBuilder.DropTable(
            name: "TestLeases");

        migrationBuilder.DropTable(
            name: "TicketDependencies");

        migrationBuilder.DropTable(
            name: "StepRuns");

        migrationBuilder.DropTable(
            name: "TicketRuns");

        migrationBuilder.DropTable(
            name: "SpecRuns");

        migrationBuilder.DropTable(
            name: "Repositories");
    }
}
