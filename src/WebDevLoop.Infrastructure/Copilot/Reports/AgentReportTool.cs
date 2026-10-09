using System.Text.Json;

namespace WebDevLoop.Infrastructure.Copilot.Reports;

/// <summary>The report tool of one agent role: its JSON parameter schema and the parser into the domain report.</summary>
internal sealed record AgentReportTool(string Name, string Description, JsonElement ParametersSchema, Func<JsonElement, ReportParseResult> Parse);
