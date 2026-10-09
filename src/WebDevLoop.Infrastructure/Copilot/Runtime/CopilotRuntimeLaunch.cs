using WebDevLoop.Core.Agents;

namespace WebDevLoop.Infrastructure.Copilot.Runtime;

/// <param name="BaseDirectory">Copilot home shared by every runtime, so sessions resume after a runtime is replaced.</param>
/// <param name="CliPath">Copilot CLI to launch; null uses the runtime bundled with the SDK package.</param>
/// <param name="Environment">Complete runtime process environment; agent shells inherit it.</param>
internal sealed record CopilotRuntimeLaunch(
    CopilotRuntimeKey Key,
    string BaseDirectory,
    string? CliPath,
    IReadOnlyDictionary<string, string> Environment);
