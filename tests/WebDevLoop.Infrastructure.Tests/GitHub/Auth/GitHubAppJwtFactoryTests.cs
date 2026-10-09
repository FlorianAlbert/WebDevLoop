using System.Text;
using System.Text.Json;
using WebDevLoop.Infrastructure.GitHub.Auth;

namespace WebDevLoop.Infrastructure.Tests.GitHub.Auth;

public sealed class GitHubAppJwtFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static GitHubAppJwtFactory CreateFactory() =>
        new(new GitHubAuthOptions { AppClientId = "Iv1.test", AppPrivateKeyPem = TestRsaKey.PrivateKeyPem }, new TestClock(Now));

    private static byte[] Base64UrlDecode(string value) =>
        Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));

    [Fact]
    public void jwt_is_rs256_signed_with_the_app_private_key()
    {
        string[] parts = CreateFactory().Create().Split('.');

        Assert.Equal(3, parts.Length);
        using JsonDocument header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
        Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());
        Assert.True(TestRsaKey.VerifyRs256($"{parts[0]}.{parts[1]}", Base64UrlDecode(parts[2])));
    }

    [Fact]
    public void jwt_claims_use_issuer_and_a_clock_skew_tolerant_short_lifetime()
    {
        string payload = CreateFactory().Create().Split('.')[1];

        using JsonDocument claims = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(payload)));
        Assert.Equal("Iv1.test", claims.RootElement.GetProperty("iss").GetString());
        Assert.Equal(Now.AddSeconds(-60).ToUnixTimeSeconds(), claims.RootElement.GetProperty("iat").GetInt64());
        Assert.Equal(Now.AddMinutes(9).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());
    }

    [Fact]
    public void creating_a_jwt_without_app_credentials_fails_clearly()
    {
        var factory = new GitHubAppJwtFactory(new GitHubAuthOptions(), new TestClock(Now));

        Assert.Throws<InvalidOperationException>(factory.Create);
    }
}
