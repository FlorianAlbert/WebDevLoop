using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Orchestration.ReviewLoop;

/// <summary>
/// Structured result of a succeeded review step: the axis report plus the round and commit it belongs to, so a round can
/// be read back after a restart or while waiting for an implementer slot.
/// </summary>
internal sealed record ReviewStepRecord(
    int Attempt,
    int Iteration,
    string ReviewedHead,
    FindingAxis Axis,
    ReviewVerdict Verdict,
    string Summary,
    IReadOnlyList<FindingRecord> Findings)
{
    public static ReviewStepRecord From(ReviewRound round, CommitSha reviewedHead, ReviewReport report) => new(
        round.Attempt,
        round.Iteration,
        reviewedHead.Value,
        report.Axis,
        report.Verdict,
        report.Summary,
        report.Findings.Select(finding => FindingRecord.From(finding)).ToArray());

    public static ReviewStepRecord? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReviewStepRecord>(json, ReviewJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public bool Matches(ReviewRound round, CommitSha reviewedHead) =>
        Attempt == round.Attempt && Iteration == round.Iteration && ReviewedHead == reviewedHead.Value;

    public ReviewReport ToReport() => new(Axis, Verdict, Summary, Findings.Select(finding => finding.ToDomain()).ToArray());

    public string ToJson() => JsonSerializer.Serialize(this, ReviewJson.Options);
}
