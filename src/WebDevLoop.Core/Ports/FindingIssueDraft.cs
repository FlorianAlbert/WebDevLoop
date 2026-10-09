using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Ports;

/// <summary>A finding ticket to create under <paramref name="Parent"/>; the adapter embeds <paramref name="Fingerprint"/> in the body.</summary>
public sealed record FindingIssueDraft(IssueRef Parent, string Title, string Body, FindingFingerprint Fingerprint);
