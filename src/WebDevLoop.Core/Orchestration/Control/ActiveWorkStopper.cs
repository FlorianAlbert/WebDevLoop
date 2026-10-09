using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Control;

/// <summary>
/// Stops the work behind steps and test leases a control command already cancelled durably: aborts their Copilot
/// sessions and kills the tester's application. Runs after the commit, so a runner that is still finishing a turn loses
/// its compare-and-swap instead of overwriting the cancellation. Failures are reported as warnings, never undo the command.
/// </summary>
public sealed class ActiveWorkStopper(IAgentRunner agents, ITestTargetRunner targets)
{
    public async Task<IReadOnlyList<string>> StopAsync(IEnumerable<StepRun> cancelledSteps, TestLease? releasedLease)
    {
        var warnings = new List<string>();
        foreach (StepRun step in cancelledSteps.Where(step => step.CopilotSessionId is not null))
        {
            try
            {
                await agents.AbortAsync(new AgentSessionId(step.CopilotSessionId!), CancellationToken.None);
            }
            catch (Exception exception)
            {
                warnings.Add($"Aborting the agent session of step '{step.Id}' failed: {exception.Message}");
            }
        }

        if (releasedLease is not null)
        {
            try
            {
                await targets.StopAsync(TestLeaseReaper.TargetOf(releasedLease), CancellationToken.None);
            }
            catch (Exception exception)
            {
                warnings.Add($"Stopping the tester application on port {releasedLease.Port} failed: {exception.Message}");
            }
        }

        return warnings;
    }
}
