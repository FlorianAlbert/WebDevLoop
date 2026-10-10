using WebDevLoop.Core.Agents;
using WebDevLoop.Web.Components.Shared;

namespace WebDevLoop.Web.Components.Runs;

/// <summary>User-facing text for identifiers shown on the run, ticket and step detail pages.</summary>
public static class RunDisplay
{
    public const int ShortHashLength = 8;

    public static string ShortHash(string? value) =>
        string.IsNullOrEmpty(value) ? "–" : value.Length > ShortHashLength + 3 ? value[..ShortHashLength] : value;

    public static string Capability(AgentCapability capability) => DisplayNames.Humanize(capability.ToString());

    public static string TokenAccess(GitHubTokenAccess access) => access switch
    {
        GitHubTokenAccess.None => "No token",
        GitHubTokenAccess.ReadOnlyIfRequired => "Read-only, only if required",
        GitHubTokenAccess.Write => "Read and write",
        _ => DisplayNames.Humanize(access.ToString()),
    };

    public static string Duration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        if (value.TotalSeconds < 60)
        {
            return $"{(int)value.TotalSeconds} s";
        }

        if (value.TotalMinutes < 60)
        {
            return value.Seconds == 0 ? $"{(int)value.TotalMinutes} min" : $"{(int)value.TotalMinutes} min {value.Seconds} s";
        }

        int hours = (int)value.TotalHours;
        return value.Minutes == 0 ? $"{hours} h" : $"{hours} h {value.Minutes} min";
    }

    public static string? Timeout(DateTimeOffset? startedAt, DateTimeOffset? timeoutAt) =>
        startedAt is { } started && timeoutAt is { } timeout ? Duration(timeout - started) : null;
}
