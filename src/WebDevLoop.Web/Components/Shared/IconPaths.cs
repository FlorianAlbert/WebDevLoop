namespace WebDevLoop.Web.Components.Shared;

/// <summary>Self-contained SVG icon bodies (24x24 viewBox) so the app needs no icon font or CDN.</summary>
public static class IconPaths
{
    private static readonly Dictionary<string, string> Bodies = new(StringComparer.Ordinal)
    {
        ["dashboard"] = """<rect width="7" height="9" x="3" y="3" rx="1"/><rect width="7" height="5" x="14" y="3" rx="1"/><rect width="7" height="9" x="14" y="12" rx="1"/><rect width="7" height="5" x="3" y="16" rx="1"/>""",
        ["repositories"] = """<path d="M4 19.5v-15A2.5 2.5 0 0 1 6.5 2H19a1 1 0 0 1 1 1v18a1 1 0 0 1-1 1H6.5a1 1 0 0 1 0-5H20"/>""",
        ["queue"] = """<path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/>""",
        ["settings"] = """<path d="M21 4h-7M10 4H3M21 12h-9M8 12H3M21 20h-5M12 20H3M14 2v4M8 10v4M16 18v4"/>""",
        ["github"] = """<path d="M6 3v12"/><circle cx="18" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M18 9a9 9 0 0 1-9 9"/>""",
        ["health"] = """<path d="M22 12h-2.48a2 2 0 0 0-1.93 1.46l-2.35 8.36a.25.25 0 0 1-.48 0L9.24 2.18a.25.25 0 0 0-.48 0l-2.35 8.36A2 2 0 0 1 4.49 12H2"/>""",
        ["menu"] = """<path d="M4 6h16M4 12h16M4 18h16"/>""",
        ["close"] = """<path d="M18 6 6 18M6 6l12 12"/>""",
        ["sun"] = """<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41"/>""",
        ["moon"] = """<path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z"/>""",
        ["monitor"] = """<rect width="20" height="14" x="2" y="3" rx="2"/><path d="M8 21h8M12 17v4"/>""",
        ["plus"] = """<path d="M5 12h14M12 5v14"/>""",
        ["refresh"] = """<path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16"/><path d="M8 16H3v5"/>""",
        ["edit"] = """<path d="M21.17 6.81a1 1 0 0 0-3.99-3.99L3.84 16.17a2 2 0 0 0-.5.83l-1.32 4.35a.5.5 0 0 0 .62.62l4.35-1.32a2 2 0 0 0 .83-.5z"/>""",
        ["trash"] = """<path d="M3 6h18M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/>""",
        ["external"] = """<path d="M15 3h6v6M10 14 21 3M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>""",
        ["check"] = """<path d="M20 6 9 17l-5-5"/>""",
        ["alert"] = """<path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3M12 9v4M12 17h.01"/>""",
        ["info"] = """<circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/>""",
        ["inbox"] = """<path d="M22 12h-6l-2 3h-4l-2-3H2"/><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/>""",
        ["chevron-down"] = """<path d="m6 9 6 6 6-6"/>""",
        ["chevron-right"] = """<path d="m9 18 6-6-6-6"/>""",
        ["search"] = """<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>""",
        ["more"] = """<circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/><circle cx="5" cy="12" r="1"/>""",
    };

    public static IEnumerable<string> Names => Bodies.Keys;

    public static bool TryGet(string? name, out string? markup)
    {
        if (name is not null && Bodies.TryGetValue(name, out string? body))
        {
            markup = body;
            return true;
        }

        markup = null;
        return false;
    }
}
