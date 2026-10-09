namespace WebDevLoop.Infrastructure.Copilot.Runtime;

/// <summary>Allow-list of runtime tools for a session; MCP tools (including the GitHub MCP server) are never available.</summary>
internal sealed record CopilotToolSelection(IReadOnlyList<string> BuiltInTools, IReadOnlyList<string> CustomTools);
