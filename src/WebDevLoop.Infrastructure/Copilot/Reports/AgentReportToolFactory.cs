using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Infrastructure.Copilot.Reports;

/// <summary>
/// Builds each role's terminal report tool. The JSON schema is generated from the wire payload, so the schema the agent
/// sees and the parser can never drift apart; domain constructors reject reports that break the contract invariants.
/// </summary>
internal static class AgentReportToolFactory
{
    private static readonly JsonSerializerOptions PayloadJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private static readonly JsonSchemaExporterOptions SchemaOptions = new() { TreatNullObliviousAsNonNullable = true };

    public static AgentReportTool For(AgentRole role) => role switch
    {
        AgentRole.Explorer => Create<ExplorationPayload>(
            AgentReportTools.Exploration, "exploration result", payload => payload.ToDomain()),
        AgentRole.Implementer => Create<ImplementationPayload>(
            AgentReportTools.Implementation, "implementation result", payload => payload.ToDomain()),
        AgentRole.ReviewerCodingStandards => Create<CodingStandardsReviewPayload>(
            AgentReportTools.Review, "coding-standards review", payload => payload.ToDomain(), FindingAxis.CodingStandards),
        AgentRole.ReviewerSpecification => Create<SpecificationReviewPayload>(
            AgentReportTools.Review, "specification review", payload => payload.ToDomain(), FindingAxis.Specification),
        AgentRole.ConflictResolver => Create<ConflictResolutionPayload>(
            AgentReportTools.ConflictResolution, "conflict resolution result", payload => payload.ToDomain()),
        AgentRole.Tester => Create<TestPayload>(
            AgentReportTools.Test, "test result", payload => payload.ToDomain()),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown agent role."),
    };

    private static AgentReportTool Create<TPayload>(
        string name,
        string subject,
        Func<TPayload, AgentReport> toDomain,
        FindingAxis? reviewAxis = null)
    {
        JsonNode schema = PayloadJson.GetJsonSchemaAsNode(typeof(TPayload), SchemaOptions);
        if (reviewAxis is { } axis)
        {
            schema["properties"]!["axis"]!["enum"] = new JsonArray(EnumValue(axis));
        }

        return new AgentReportTool(
            name,
            $"Report the final {subject} to WebDevLoop. Call this tool exactly once, as your final action; it ends your turn.",
            JsonSerializer.SerializeToElement(schema),
            arguments => Parse(arguments, toDomain, reviewAxis));
    }

    private static ReportParseResult Parse<TPayload>(JsonElement arguments, Func<TPayload, AgentReport> toDomain, FindingAxis? reviewAxis)
    {
        try
        {
            TPayload payload = arguments.Deserialize<TPayload>(PayloadJson)
                ?? throw new InvalidAgentReportException("The report payload must be a JSON object.");
            AgentReport report = toDomain(payload);
            if (reviewAxis is { } axis && report is ReviewReport review && review.Axis != axis)
            {
                return ReportParseResult.Rejected($"This reviewer covers the '{EnumValue(axis)}' axis only.");
            }

            return ReportParseResult.Accepted(report);
        }
        catch (Exception exception) when (exception is JsonException or InvalidAgentReportException)
        {
            return ReportParseResult.Rejected(exception.Message);
        }
    }

    private static string EnumValue<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonSerializer.SerializeToElement(value, PayloadJson).GetString()!;
}
