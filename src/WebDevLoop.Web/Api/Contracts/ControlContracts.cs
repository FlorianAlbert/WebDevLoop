namespace WebDevLoop.Web.Api.Contracts;

/// <summary>An applied control command and the run as it is now.</summary>
/// <param name="Warnings">Cleanup that failed after the command was applied (e.g. an agent session that could not be aborted).</param>
public sealed record RunControlResponse<TRun>(string Action, TRun Run, IReadOnlyList<string> Warnings);
