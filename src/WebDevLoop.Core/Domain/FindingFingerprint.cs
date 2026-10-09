using System.Text.RegularExpressions;

namespace WebDevLoop.Core.Domain;

/// <summary>Case- and whitespace-insensitive identity of a finding, used to find already-created tickets.</summary>
public readonly partial record struct FindingFingerprint
{
    public FindingFingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Fingerprint must not be blank.", nameof(value));
        }

        Value = Whitespace().Replace(value.Trim(), " ").ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
