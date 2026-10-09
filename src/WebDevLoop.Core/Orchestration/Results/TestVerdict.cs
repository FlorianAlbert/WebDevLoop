namespace WebDevLoop.Core.Orchestration.Results;

public enum TestVerdict
{
    /// <summary>Every requirement verified, no issues.</summary>
    Pass,

    IssuesFound,

    /// <summary>The app could not be started or tested because of the environment or run instructions, not a product defect.</summary>
    Blocked,
}
