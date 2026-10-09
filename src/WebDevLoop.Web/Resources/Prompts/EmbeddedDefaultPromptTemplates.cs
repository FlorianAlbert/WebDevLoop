using System.Reflection;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Web.Resources.Prompts;

/// <summary>Default prompt templates shipped as embedded <c>Resources/Prompts/&lt;AgentRole&gt;.md</c> files.</summary>
public sealed class EmbeddedDefaultPromptTemplates : IDefaultPromptTemplates
{
    private const string ResourcePrefix = "WebDevLoop.Web.Resources.Prompts.";
    private const string ResourceExtension = ".md";

    private readonly IReadOnlyDictionary<AgentRole, string> _templates;

    public EmbeddedDefaultPromptTemplates()
    {
        Assembly assembly = typeof(EmbeddedDefaultPromptTemplates).Assembly;
        _templates = Enum.GetValues<AgentRole>().ToDictionary(role => role, role => Load(assembly, role));
    }

    public string GetTemplate(AgentRole role) => _templates[role];

    public static string ResourceName(AgentRole role) => ResourcePrefix + role + ResourceExtension;

    private static string Load(Assembly assembly, AgentRole role)
    {
        string name = ResourceName(role);
        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded default prompt template '{name}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
