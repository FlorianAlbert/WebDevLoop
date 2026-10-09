using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Findings;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Core.Tests.Orchestration.TicketExecution;

namespace WebDevLoop.Core.Tests.Orchestration.Findings;

/// <summary>Finding-ticket issuance over the CAS workflow database, issuance records, and an in-memory issue tracker.</summary>
internal sealed class FindingsFixture
{
    public static readonly StepRunId SpecificationStep = new("parent-review-specification-1");
    public static readonly StepRunId StandardsStep = new("parent-review-coding-standards-1");

    public TicketExecutionFixture Execution { get; } = new();

    public FindingIssuanceStore Issuances { get; } = new();

    public FindingIssueTracker Issues { get; } = new();

    public static CancellationToken Token => TicketExecutionFixture.Token;

    public FindingWorkflowScope OpenScope() => new(Execution.Db.OpenScope(), Issuances);

    public FindingTicketIssuer Issuer(FindingWorkflowScope scope) =>
        new(scope.Workflow, scope, Issues, scope, Execution.Ids, Execution.Clock);

    public async Task<FindingIssuanceResult> IssueAsync(RunId specRunId, params SourcedFinding[] findings)
    {
        FindingWorkflowScope scope = OpenScope();
        SpecRun spec = (await scope.Workflow.GetAsync(specRunId, Token))!;
        return await Issuer(scope).IssueAsync(spec, findings, Token);
    }

    public static SourcedFinding ParentReview(Finding finding) =>
        new(StepKind.ParentReview, finding.Axis == FindingAxis.CodingStandards ? StandardsStep : SpecificationStep, finding);

    public static SpecificationFinding Missing(string description, string file = "src/Feature.cs", int? line = null) =>
        new(SpecificationFindingKind.Missing, "Errors must be logged.", file, line, description, "Implement it as specified.");

    public static CodingStandardsFinding MagicNumber(string file = "src/Feature.cs") =>
        new(CodingStandardsSeverity.Blocking, file, 12, "var delay = 42;", "CONTRIBUTING.md: no magic values", "Magic number 42 in Feature.", "Name the constant.");

    public TicketRun Ticket(TicketRunId id) => Execution.Ticket(id);

    public IReadOnlyList<TicketRun> Tickets(RunId specRunId) =>
        Execution.Db.Rows<TicketRun>().Where(ticket => ticket.SpecRunId == specRunId).OrderBy(ticket => ticket.Issue.Number).ToArray();

    public IReadOnlyList<TicketDependency> FindingDependencies(RunId specRunId) =>
        Execution.Db.TicketDependencies.Where(edge => edge.SpecRunId == specRunId && edge.Source == DependencySource.CreatedFinding).ToArray();
}
