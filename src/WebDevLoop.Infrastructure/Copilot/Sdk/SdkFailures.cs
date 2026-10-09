using System.Text.RegularExpressions;

namespace WebDevLoop.Infrastructure.Copilot.Sdk;

/// <summary>The SDK reports auth problems as plain errors; they are recognised by status code and wording.</summary>
internal static partial class SdkFailures
{
    public static bool IsAuthenticationFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is not OperationCanceledException && IsAuthenticationMessage(current.Message))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsAuthenticationMessage(string? message) => message is not null && AuthenticationWording().IsMatch(message);

    [GeneratedRegex(@"\b401\b|unauthori[sz]ed|not authenticated|authentication|bad credentials", RegexOptions.IgnoreCase)]
    private static partial Regex AuthenticationWording();
}
