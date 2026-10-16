using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.Prerequisites;

/// <summary>Validates every role's effective Model and Reasoning Effort against the Model Catalog.</summary>
public sealed class AgentModelsCheck(
    IModelCatalog catalog,
    Func<CancellationToken, Task<IReadOnlyList<AgentModelSettingsScope>>> readSettings) : IPrerequisiteCheck
{
    public const string CheckName = "Agent models";
    public const string SettingsPath = "/settings#section-roles";

    public string Name => CheckName;

    public string RunsAfter => GitHubAuthCheck.CheckName;

    public async Task<PrerequisiteCheck> RunAsync(CancellationToken cancellationToken)
    {
        if (!catalog.HasFetched)
        {
            return CheckResult.Failed(Name, "The Model Catalog has never been fetched successfully.",
                $"Retry fetching the Model Catalog and choose available Models in settings ({SettingsPath}).");
        }

        var models = catalog.Models.ToDictionary(model => model.Id, StringComparer.Ordinal);
        IReadOnlyList<AgentModelSettingsScope> scopes = await readSettings(cancellationToken);
        string[] invalid = scopes.SelectMany(scope => ModelSelectionValidator.Validate(scope, models)).ToArray();

        return invalid.Length == 0
            ? CheckResult.Passed(Name, "All configured agent Models and Reasoning Efforts are available.")
            : CheckResult.Failed(Name, string.Join(Environment.NewLine, invalid),
                $"Choose available Models and supported Reasoning Efforts in settings ({SettingsPath}).");
    }
}
