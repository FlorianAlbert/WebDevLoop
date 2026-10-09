using System.Security.Cryptography;
using System.Text;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.GitHub.Issues;

/// <summary>
/// Hidden HTML comment embedded in finding issue bodies. The fingerprint is hashed so arbitrary text can never
/// terminate the comment or leak into the rendered issue.
/// </summary>
public static class FindingFingerprintMarker
{
    private const string Prefix = "<!-- webdevloop:fingerprint:sha256:";
    private const string Suffix = " -->";

    public static string Render(FindingFingerprint fingerprint) => $"{Prefix}{Hash(fingerprint)}{Suffix}";

    public static bool IsPresentIn(string? body, FindingFingerprint fingerprint) =>
        body is not null && body.Contains(Render(fingerprint), StringComparison.Ordinal);

    private static string Hash(FindingFingerprint fingerprint) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.Value)));
}
