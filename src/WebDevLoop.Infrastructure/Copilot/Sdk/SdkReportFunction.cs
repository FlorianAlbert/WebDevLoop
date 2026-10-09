using System.Text.Json;
using GitHub.Copilot;
using Microsoft.Extensions.AI;
using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot.Sdk;

/// <summary>
/// Exposes a report tool to the runtime with its explicit JSON schema. Terminal and permission-free flags are taken from
/// <see cref="CopilotTool.DefineTool"/>, so the SDK's own metadata keys are used.
/// </summary>
internal sealed class SdkReportFunction : AIFunction
{
    private static readonly IReadOnlyDictionary<string, object?> ReportToolMetadata = CopilotTool.DefineTool(
            () => string.Empty,
            new CopilotToolOptions { IsTerminal = true, SkipPermission = true })
        .AdditionalProperties;

    private readonly CopilotReportTool _tool;

    public SdkReportFunction(CopilotReportTool tool)
    {
        _tool = tool;
    }

    public override string Name => _tool.Name;

    public override string Description => _tool.Description;

    public override JsonElement JsonSchema => _tool.ParametersSchema;

    public override IReadOnlyDictionary<string, object?> AdditionalProperties => ReportToolMetadata;

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(
            arguments.ToDictionary(argument => argument.Key, argument => argument.Value, StringComparer.Ordinal));
        return ValueTask.FromResult<object?>(_tool.Invoke(payload));
    }
}
