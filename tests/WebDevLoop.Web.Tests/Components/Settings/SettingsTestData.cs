using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Settings;

namespace WebDevLoop.Web.Tests.Components.Settings;

internal static class SettingsTestData
{
    public const string DefaultTemplate = "Default template for {repo_owner}/{repo_name}";

    public static EffectiveSettings Defaults { get; } = DefaultSettings.Create(new FixedTemplates(), "/data");

    private sealed class FixedTemplates : IDefaultPromptTemplates
    {
        public string GetTemplate(AgentRole role) => DefaultTemplate;
    }
}
