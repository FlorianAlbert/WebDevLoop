using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Tests.Persistence;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public static readonly CommitSha Sha1 = new(new string('a', 40));

    public static readonly CommitSha Sha2 = new(new string('b', 40));

    public static RepositoryRecord NewRepository(string owner = "acme", string name = "widgets") =>
        RepositoryRecord.Register(new GitHubRepoRef(owner, name), new BranchName("main"), $"https://github.com/{owner}/{name}.git", $"/work/{owner}/{name}", Now);

    public static SpecRun NewSpecRun(int repositoryId, string id = "run-1", int issueNumber = 10, int queuePosition = 1) =>
        SpecRun.Queue(new RunId(id), repositoryId, new IssueRef("acme", "widgets", issueNumber, "I_node", 99), "Spec title", "Spec body", queuePosition, Now);

    public static TicketRun NewTicketRun(RunId specRunId, string id = "t-1", int issueNumber = 11) =>
        TicketRun.Create(new TicketRunId(id), specRunId, new IssueRef("acme", "widgets", issueNumber), "Ticket title", "Ticket body", Now);

    public static StepRun NewStep(RunId specRunId, TicketRunId? ticketRunId, string id, StepKind kind, AgentRole? role = null) =>
        StepRun.Create(new StepRunId(id), specRunId, ticketRunId, kind, role, 1, "prompt-hash");

    /// <summary>Persists a repository with one spec run in the given status path and returns the spec run id.</summary>
    public static async Task<(int RepositoryId, RunId SpecRunId)> SeedSpecRunAsync(PersistenceHarness harness, string specRunId = "run-1")
    {
        using PersistenceScope scope = harness.OpenScope();
        RepositoryRecord repository = NewRepository();
        scope.Repositories.Add(repository);
        await scope.SaveAsync();

        SpecRun specRun = NewSpecRun(repository.Id, specRunId);
        scope.SpecRuns.Add(specRun);
        await scope.SaveAsync();

        return (repository.Id, specRun.Id);
    }

    public static async Task<(RunId SpecRunId, TicketRunId TicketRunId)> SeedTicketRunAsync(PersistenceHarness harness)
    {
        (int _, RunId specRunId) = await SeedSpecRunAsync(harness);
        using PersistenceScope scope = harness.OpenScope();
        TicketRun ticket = NewTicketRun(specRunId);
        scope.Tickets.Add(ticket);
        await scope.SaveAsync();

        return (specRunId, ticket.Id);
    }
}
