namespace WebDevLoop.Core.Orchestration.Attention;

/// <summary>Starts the resolution pipeline for a run or ticket that needs attention, in the background and at most once at a time per item.</summary>
public interface IAttentionTriageLauncher
{
    void Launch(AttentionTriageAssignment assignment);
}
