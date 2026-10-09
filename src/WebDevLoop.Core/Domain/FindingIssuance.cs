namespace WebDevLoop.Core.Domain;

/// <summary>Recorded before and after issue creation so recovery never creates a finding ticket twice.</summary>
public sealed class FindingIssuance
{
    private FindingIssuance()
    {
    }

    public long Id { get; private set; }

    public RunId SpecRunId { get; private set; }

    public StepRunId SourceStepRunId { get; private set; }

    public FindingAxis Axis { get; private set; }

    public FindingFingerprint Fingerprint { get; private set; }

    public int? IssueNumber { get; private set; }

    public long? IssueDatabaseId { get; private set; }

    public FindingIssuanceStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static FindingIssuance Plan(RunId specRunId, StepRunId sourceStepRunId, FindingAxis axis, FindingFingerprint fingerprint, DateTimeOffset at) => new()
    {
        SpecRunId = specRunId,
        SourceStepRunId = sourceStepRunId,
        Axis = axis,
        Fingerprint = fingerprint,
        CreatedAt = at,
        UpdatedAt = at,
    };

    public void RecordCreated(int issueNumber, long issueDatabaseId, DateTimeOffset at)
    {
        if (Status == FindingIssuanceStatus.Created)
        {
            if (IssueNumber == issueNumber && IssueDatabaseId == issueDatabaseId)
            {
                return;
            }

            throw new InvalidOperationException($"Finding already has issue #{IssueNumber}; refusing to record #{issueNumber}.");
        }

        IssueNumber = issueNumber;
        IssueDatabaseId = issueDatabaseId;
        Status = FindingIssuanceStatus.Created;
        UpdatedAt = at;
    }
}
