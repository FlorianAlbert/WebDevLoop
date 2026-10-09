namespace WebDevLoop.Core.Ports;

public enum GitRefScope
{
    /// <summary>refs/heads/* in the local clone.</summary>
    Local,

    /// <summary>refs/remotes/origin/* as of the last fetch.</summary>
    Remote,
}
