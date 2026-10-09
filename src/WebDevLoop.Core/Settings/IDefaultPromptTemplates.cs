using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Settings;

/// <summary>Source of the shipped default prompt template per agent role (embedded resources in the web host).</summary>
public interface IDefaultPromptTemplates
{
    string GetTemplate(AgentRole role);
}
