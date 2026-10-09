using System.Text.Json;

namespace WebDevLoop.Infrastructure.Copilot.Runtime;

/// <summary>A terminal, permission-free custom tool; <paramref name="Invoke"/> receives the raw arguments and returns the tool result.</summary>
internal sealed record CopilotReportTool(string Name, string Description, JsonElement ParametersSchema, Func<JsonElement, string> Invoke);
