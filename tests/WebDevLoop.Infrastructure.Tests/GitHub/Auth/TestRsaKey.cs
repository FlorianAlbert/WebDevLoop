using System.Security.Cryptography;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Auth;

/// <summary>Throw-away RSA key generated per test run; never a real GitHub App key.</summary>
internal static class TestRsaKey
{
    private static readonly RSA Rsa = RSA.Create(2048);

    public static string PrivateKeyPem { get; } = Rsa.ExportPkcs8PrivateKeyPem();

    public static bool VerifyRs256(string signingInput, byte[] signature) =>
        Rsa.VerifyData(System.Text.Encoding.ASCII.GetBytes(signingInput), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
}
