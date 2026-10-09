namespace WebDevLoop.Core.Domain;

public readonly record struct CommitSha
{
    private const int Sha1Length = 40;
    private const int Sha256Length = 64;

    public CommitSha(string value)
    {
        if (value is null || value.Length is not (Sha1Length or Sha256Length) || !value.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("A commit SHA must be 40 or 64 hexadecimal characters.", nameof(value));
        }

        Value = value.ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
