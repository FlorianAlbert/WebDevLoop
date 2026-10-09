namespace WebDevLoop.Core.Ports;

public enum RefUpdateOutcome
{
    Updated,

    /// <summary>The ref already pointed at the requested commit (safe replay).</summary>
    AlreadyAtTarget,

    /// <summary>The ref moved since it was read; nothing was changed.</summary>
    ExpectedPriorMismatch,
}
