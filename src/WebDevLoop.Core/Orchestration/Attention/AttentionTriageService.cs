using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Orchestration.Attention;

public enum AttentionTriageOutcome
{
    /// <summary>The run or ticket does not need attention (any more) or was already triaged; nothing was done.</summary>
    NothingToDo,

    /// <summary>A stage resolved the situation and the work was resumed.</summary>
    Resolved,

    /// <summary>No stage resolved it; the guidance (with what was tried) stays for the user.</summary>
    NeedsUser,

    /// <summary>A save lost a compare-and-swap race; the other writer wins.</summary>
    ConcurrencyConflict,
}

/// <summary>
/// The resolution order for a run or ticket that entered <c>NeedsAttention</c>: every registered <see cref="IAttentionStage"/> in
/// order (known remediation first; a troubleshooter agent session is another stage registered after it), and only when none
/// resolves the situation does it stay with the user, now with everything the stages tried on the "Action needed" card.
/// A stage that resolves the situation ends the pipeline and the work resumes exactly as if the user had pressed Retry (or Skip);
/// the run history records what was done and why.
/// </summary>
public sealed class AttentionTriageService(
    IEnumerable<IAttentionStage> stages,
    ISpecRunRepository specRuns,
    ITicketRunRepository ticketRuns,
    IRunEventRepository runEvents,
    SpecRunControl specControl,
    TicketRunControl ticketControl,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private readonly IAttentionStage[] _stages = [.. stages];

    public async Task<AttentionTriageOutcome> TriageAsync(AttentionTriageAssignment assignment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (await LoadCaseAsync(assignment, cancellationToken) is not { } attentionCase)
        {
            return AttentionTriageOutcome.NothingToDo;
        }

        runEvents.Add(AttentionRunEvents.Raise(attentionCase, clock.UtcNow));
        var tried = new List<string>();
        AttentionStageDiagnosis? diagnosis = null;
        foreach (IAttentionStage stage in _stages)
        {
            AttentionStageResult result = await stage.TryAsync(attentionCase, cancellationToken);
            tried.AddRange(result.Tried.Where(_ => result.Status != AttentionStageStatus.NotApplicable));
            diagnosis = result.Diagnosis ?? diagnosis;
            if (result.Status == AttentionStageStatus.Resolved)
            {
                return await ResumeAsync(attentionCase, stage, result, cancellationToken);
            }
        }

        return await AskUserAsync(attentionCase, tried, diagnosis, cancellationToken);
    }

    /// <returns>Null unless the owner still needs attention and its guidance was not triaged yet.</returns>
    private async Task<AttentionCase?> LoadCaseAsync(AttentionTriageAssignment assignment, CancellationToken cancellationToken)
    {
        AttentionReason? reason;
        if (assignment.TicketRunId is { } ticketId)
        {
            TicketRun? ticket = await ticketRuns.GetAsync(ticketId, cancellationToken);
            reason = ticket is { Status: TicketRunStatus.NeedsAttention } ? ticket.Attention : null;
        }
        else
        {
            SpecRun? spec = await specRuns.GetAsync(assignment.SpecRunId, cancellationToken);
            reason = spec is { Status: SpecRunStatus.NeedsAttention } ? spec.Attention : null;
        }

        return reason is null or { AutoFix.Attempted: true } ? null : new AttentionCase(assignment.SpecRunId, assignment.TicketRunId, reason);
    }

    private async Task<AttentionTriageOutcome> ResumeAsync(AttentionCase attentionCase, IAttentionStage stage, AttentionStageResult result, CancellationToken cancellationToken)
    {
        runEvents.Add(AttentionRunEvents.Resolve(attentionCase, stage.Name, result, clock.UtcNow));
        // The remediation may have changed the ticket (e.g. a merged commit); the control command saves it with its own transition.
        ControlResult control = (attentionCase.TicketRunId, result.Resume!.Kind) switch
        {
            ({ } ticketId, AttentionResumeKind.SkipTicket) => await ticketControl.SkipAsync(ticketId, SkipDependents.Unblock, cancellationToken, automatic: true),
            ({ } ticketId, _) => await ticketControl.RetryAsync(ticketId, cancellationToken, automatic: true, result.Resume.RetryAt),
            _ => await specControl.RetryAsync(attentionCase.SpecRunId, cancellationToken, automatic: true),
        };
        return control.Outcome switch
        {
            ControlOutcome.Applied => AttentionTriageOutcome.Resolved,
            ControlOutcome.ConcurrencyConflict => AttentionTriageOutcome.ConcurrencyConflict,
            _ => await KeepForUserAsync(attentionCase, [..result.Tried, $"Could not resume automatically: {control.Reason}"], result.Diagnosis, cancellationToken),
        };
    }

    private async Task<AttentionTriageOutcome> AskUserAsync(
        AttentionCase attentionCase,
        IReadOnlyList<string> tried,
        AttentionStageDiagnosis? diagnosis,
        CancellationToken cancellationToken)
    {
        if (tried.Count == 0 && attentionCase.Reason.AutoFix is null && diagnosis is null)
        {
            // Nothing was attempted: only the "raised" event is worth keeping.
            return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
                ? AttentionTriageOutcome.NeedsUser
                : AttentionTriageOutcome.ConcurrencyConflict;
        }

        return await KeepForUserAsync(attentionCase, tried, diagnosis, cancellationToken);
    }

    private async Task<AttentionTriageOutcome> KeepForUserAsync(
        AttentionCase attentionCase,
        IReadOnlyList<string> tried,
        AttentionStageDiagnosis? diagnosis,
        CancellationToken cancellationToken)
    {
        AttentionReason updated = attentionCase.Reason.WithTried([.. tried]);
        if (diagnosis is not null)
        {
            updated = updated.WithDiagnosis(diagnosis.Diagnosis, diagnosis.UserSteps, diagnosis.SuggestedButtons);
        }

        if (updated.AutoFix is not null)
        {
            updated = updated.WithAutoFixAttempted(tried.Count == 0 ? "Not applicable." : tried[^1]);
        }

        runEvents.Add(AttentionRunEvents.AskUser(attentionCase, tried, clock.UtcNow));
        if (attentionCase.TicketRunId is { } ticketId)
        {
            if (await ticketRuns.GetAsync(ticketId, cancellationToken) is { Status: TicketRunStatus.NeedsAttention } ticket)
            {
                ticket.UpdateAttention(updated, clock.UtcNow);
            }
        }
        else if (await specRuns.GetAsync(attentionCase.SpecRunId, cancellationToken) is { Status: SpecRunStatus.NeedsAttention } spec)
        {
            spec.UpdateAttention(updated);
        }

        return await unitOfWork.SaveChangesAsync(cancellationToken) == SaveOutcome.Saved
            ? AttentionTriageOutcome.NeedsUser
            : AttentionTriageOutcome.ConcurrencyConflict;
    }
}
