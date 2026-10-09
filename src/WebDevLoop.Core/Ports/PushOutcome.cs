namespace WebDevLoop.Core.Ports;

public enum PushOutcome
{
    Pushed,

    /// <summary>The remote ref already pointed at the commit (safe replay).</summary>
    AlreadyUpToDate,

    /// <summary>The remote ref did not match the expected tip; nothing was pushed.</summary>
    Rejected,
}
