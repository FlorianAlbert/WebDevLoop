namespace WebDevLoop.Web.Components.Shared;

public static class PageTitles
{
    public const string AppName = "WebDevLoop";

    public static string Format(string? pageTitle) =>
        string.IsNullOrWhiteSpace(pageTitle) ? AppName : $"{pageTitle} · {AppName}";
}
