namespace WebDevLoop.Core.Ports;

public enum StackMergeStatus
{
    Open,

    /// <summary>All layers merged and trunk contains the top layer.</summary>
    Merged,

    /// <summary>The stack was closed (or a layer closed) without reaching trunk.</summary>
    ClosedUnmerged,
}
