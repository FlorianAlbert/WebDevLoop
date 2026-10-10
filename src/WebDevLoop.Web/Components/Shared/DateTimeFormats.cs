using System.Globalization;

namespace WebDevLoop.Web.Components.Shared;

public enum DateTimeDisplay
{
    Relative,
    Absolute,
}

public static class DateTimeFormats
{
    /// <summary>Local time, e.g. "2026-10-10 12:27".</summary>
    public static string Absolute(DateTimeOffset value, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(value, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string Relative(DateTimeOffset value, DateTimeOffset now, TimeZoneInfo zone)
    {
        TimeSpan age = now - value;
        if (age < TimeSpan.Zero || age > TimeSpan.FromDays(7))
        {
            return Absolute(value, zone);
        }

        if (age < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes} min ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            int hours = (int)age.TotalHours;
            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }

        int days = (int)age.TotalDays;
        return days == 1 ? "yesterday" : $"{days} days ago";
    }
}
