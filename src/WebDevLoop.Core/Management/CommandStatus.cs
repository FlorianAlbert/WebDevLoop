namespace WebDevLoop.Core.Management;

public enum CommandStatus
{
    Succeeded,
    NotFound,

    /// <summary>The request failed validation; <see cref="CommandResult{T}.Errors"/> names the offending fields.</summary>
    Invalid,

    /// <summary>The request is valid but collides with current state (duplicate, in use, or a lost concurrency race).</summary>
    Conflict,
}
