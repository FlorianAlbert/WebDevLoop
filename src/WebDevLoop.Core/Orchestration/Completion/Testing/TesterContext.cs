using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Orchestration.Completion.Testing;

/// <summary>Everything one tester run needs, loaded once per run.</summary>
/// <param name="Head">The integration tip under test.</param>
internal sealed record TesterContext(
    SpecRun Spec,
    RepositoryRecord Repository,
    EffectiveSettings Settings,
    CommitSha Head,
    TestWorkspace Workspace,
    IReadOnlyList<TicketRun> Tickets,
    IReadOnlyList<TicketDependency> Dependencies);
