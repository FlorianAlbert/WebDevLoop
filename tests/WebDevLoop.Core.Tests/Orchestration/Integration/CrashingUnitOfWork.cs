using WebDevLoop.Core.Ports;

namespace WebDevLoop.Core.Tests.Orchestration.Integration;

/// <summary>Commits through <paramref name="inner"/>, then crashes the process right after save number <paramref name="crashAfterSave"/>.</summary>
internal sealed class CrashingUnitOfWork(IUnitOfWork inner, ExternalCallJournal journal, int? crashAfterSave = null) : IUnitOfWork
{
    private int _saves;

    public async Task<SaveOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        journal.ThrowIfDown();
        SaveOutcome outcome = await inner.SaveChangesAsync(cancellationToken);
        if (++_saves == crashAfterSave)
        {
            journal.Crash($"after save {_saves}");
        }

        return outcome;
    }
}
