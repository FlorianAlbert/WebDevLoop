namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>The implementer's response to one review finding (by the app-assigned finding id) in a fix turn.</summary>
public sealed record AddressedFinding
{
    public AddressedFinding(string findingId, string response)
    {
        FindingId = ReportGuard.RequireText(findingId, nameof(findingId));
        Response = ReportGuard.RequireText(response, nameof(response));
    }

    public string FindingId { get; }

    /// <summary>How the finding was resolved, or why the implementer disagrees.</summary>
    public string Response { get; }
}
