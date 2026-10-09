namespace WebDevLoop.Core.Orchestration.Results;

/// <summary>One actionable review or test finding; becomes a finding ticket when raised by a parent review or tester.</summary>
/// <param name="Location">File/line or UI location, when known.</param>
/// <param name="Reproduction">Steps to reproduce (tester findings).</param>
public sealed record Finding
{
    public Finding(string title, string details, string? location = null, string? reproduction = null)
    {
        Title = ReportGuard.RequireText(title, nameof(title));
        Details = ReportGuard.RequireText(details, nameof(details));
        Location = location;
        Reproduction = reproduction;
    }

    public string Title { get; }

    public string Details { get; }

    public string? Location { get; }

    public string? Reproduction { get; }
}
