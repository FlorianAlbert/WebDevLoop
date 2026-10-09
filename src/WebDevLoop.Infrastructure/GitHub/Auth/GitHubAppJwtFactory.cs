using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.GitHub.Auth;

/// <summary>Creates short-lived RS256 JWTs that authenticate as the GitHub App itself.</summary>
public sealed class GitHubAppJwtFactory(GitHubAuthOptions options, IClock clock)
{
    // GitHub rejects tokens that are issued in the future (clock drift) or live longer than 10 minutes.
    private static readonly TimeSpan IssuedAtBackdate = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(9);

    public string Create()
    {
        if (!options.IsAppConfigured)
        {
            throw new InvalidOperationException("GitHub App client id and private key are not configured.");
        }

        DateTimeOffset now = clock.UtcNow;
        string header = EncodeJson(new { alg = "RS256", typ = "JWT" });
        string payload = EncodeJson(new
        {
            iss = options.AppClientId,
            iat = (now - IssuedAtBackdate).ToUnixTimeSeconds(),
            exp = (now + Lifetime).ToUnixTimeSeconds(),
        });
        string signingInput = $"{header}.{payload}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(options.AppPrivateKeyPem);
        byte[] signature = rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private static string EncodeJson<T>(T value) => Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(value));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
