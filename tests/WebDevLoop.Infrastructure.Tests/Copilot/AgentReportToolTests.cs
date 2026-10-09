using System.Text.Json;
using WebDevLoop.Core.Agents;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Results;
using WebDevLoop.Infrastructure.Copilot.Reports;

namespace WebDevLoop.Infrastructure.Tests.Copilot;

public sealed class AgentReportToolTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    public static TheoryData<AgentRole> AllRoles => [.. Enum.GetValues<AgentRole>()];

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void each_role_reports_through_the_tool_named_by_its_policy(AgentRole role)
    {
        RoleCapabilityPolicy policy = RoleCapabilityPolicies.For(role, new AgentWorkspace("/work/repo", "/work/notes"));

        AgentReportTool tool = AgentReportToolFactory.For(role);

        Assert.Equal(policy.ReportToolName, tool.Name);
        Assert.Contains("exactly once", tool.Description);
        Assert.Equal("object", tool.ParametersSchema.GetProperty("type").GetString());
    }

    [Fact]
    public void test_report_schema_matches_the_test_report_contract()
    {
        JsonElement schema = AgentReportToolFactory.For(AgentRole.Tester).ParametersSchema;

        Assert.Equal(["verdict", "summary", "scenarios", "issues"], PropertyNames(schema));
        Assert.Equal(["verdict", "summary", "scenarios", "issues"], RequiredNames(schema));
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["pass", "issues_found", "blocked"], EnumValues(Property(schema, "verdict")));
        JsonElement scenario = Items(Property(schema, "scenarios"));
        Assert.Equal(["name", "outcome", "notes"], PropertyNames(scenario));
        Assert.Equal(["passed", "failed", "not_run"], EnumValues(Property(scenario, "outcome")));
        Assert.Equal(["name", "outcome"], RequiredNames(scenario));
        JsonElement issue = Items(Property(schema, "issues"));
        Assert.Equal(["title", "severity", "spec_reference", "steps_to_reproduce", "expected", "actual", "evidence"], PropertyNames(issue));
        Assert.Equal(["critical", "major", "minor"], EnumValues(Property(issue, "severity")));
    }

    [Fact]
    public void implementation_report_schema_matches_the_implementation_contract()
    {
        JsonElement schema = AgentReportToolFactory.For(AgentRole.Implementer).ParametersSchema;

        Assert.Equal(["status", "head_commit_sha", "summary", "tests", "addressed_findings", "follow_ups"], PropertyNames(schema));
        Assert.Equal(["completed", "blocked"], EnumValues(Property(schema, "status")));
        Assert.Equal(["command", "result"], PropertyNames(Items(Property(schema, "tests"))));
        Assert.Equal(["finding_id", "response"], PropertyNames(Items(Property(schema, "addressed_findings"))));
    }

    [Fact]
    public void conflict_and_exploration_report_schemas_match_their_contracts()
    {
        JsonElement conflict = AgentReportToolFactory.For(AgentRole.ConflictResolver).ParametersSchema;
        JsonElement exploration = AgentReportToolFactory.For(AgentRole.Explorer).ParametersSchema;

        Assert.Equal(["status", "head_commit_sha", "resolved_files", "summary", "tests"], PropertyNames(conflict));
        Assert.Equal(["resolved", "blocked"], EnumValues(Property(conflict, "status")));
        Assert.Equal(["command", "result"], PropertyNames(Items(Property(conflict, "tests"))));
        Assert.Equal(["status", "summary", "notes_files"], PropertyNames(exploration));
    }

    [Theory]
    [InlineData(AgentRole.ReviewerCodingStandards, "coding_standards", new[] { "severity", "file", "line", "evidence", "rule", "description", "recommendation" })]
    [InlineData(AgentRole.ReviewerSpecification, "specification", new[] { "kind", "spec_reference", "file", "line", "description", "recommendation" })]
    public void review_report_schema_is_pinned_to_the_reviewer_axis(AgentRole role, string axis, string[] findingFields)
    {
        JsonElement schema = AgentReportToolFactory.For(role).ParametersSchema;

        Assert.Equal(["axis", "verdict", "summary", "findings"], PropertyNames(schema));
        Assert.Equal([axis], EnumValues(Property(schema, "axis")));
        Assert.Equal(["clean", "issues_found"], EnumValues(Property(schema, "verdict")));
        Assert.Equal(findingFields, PropertyNames(Items(Property(schema, "findings"))));
    }

    [Fact]
    public void valid_implementation_payload_becomes_an_implementation_report()
    {
        ReportParseResult result = Parse(AgentRole.Implementer, $$"""
            {
              "status": "completed",
              "head_commit_sha": "{{Sha}}",
              "summary": "Added the parser.",
              "tests": [{ "command": "dotnet test", "result": "42 passed" }],
              "addressed_findings": [{ "finding_id": "cs-1", "response": "Renamed." }],
              "follow_ups": []
            }
            """);

        var report = Assert.IsType<ImplementationReport>(result.Report);
        Assert.Null(result.Error);
        Assert.Equal(ReportStatus.Completed, report.Status);
        Assert.Equal(new CommitSha(Sha), report.HeadCommitSha);
        Assert.Equal(new CommandResult("dotnet test", "42 passed"), Assert.Single(report.Tests));
        Assert.Equal("cs-1", Assert.Single(report.AddressedFindings).FindingId);
    }

    [Fact]
    public void valid_test_payload_becomes_a_test_report()
    {
        ReportParseResult result = Parse(AgentRole.Tester, """
            {
              "verdict": "issues_found",
              "summary": "Login breaks.",
              "scenarios": [{ "name": "login", "outcome": "failed" }, { "name": "logout", "outcome": "not_run", "notes": "blocked by login" }],
              "issues": [{
                "title": "Login returns 500",
                "severity": "critical",
                "spec_reference": null,
                "steps_to_reproduce": ["Open /login", "Submit"],
                "expected": "Dashboard",
                "actual": "HTTP 500",
                "evidence": ["test-evidence/attempt-1/login.png"]
              }]
            }
            """);

        var report = Assert.IsType<TestReport>(result.Report);
        Assert.Equal(TestVerdict.IssuesFound, report.Verdict);
        Assert.Equal(
            [new TestScenario("login", TestScenarioOutcome.Failed), new TestScenario("logout", TestScenarioOutcome.NotRun, "blocked by login")],
            report.Scenarios);
        Assert.Equal(TestIssueSeverity.Critical, Assert.Single(report.Issues).Severity);
    }

    [Fact]
    public void valid_coding_standards_review_becomes_a_review_report()
    {
        ReportParseResult result = Parse(AgentRole.ReviewerCodingStandards, """
            {
              "axis": "coding_standards",
              "verdict": "issues_found",
              "summary": "One smell.",
              "findings": [{
                "severity": "judgement", "file": "src/A.cs", "line": 12, "evidence": "var x = 42;",
                "rule": "No magic values", "description": "Unexplained 42.", "recommendation": "Name the constant."
              }]
            }
            """);

        var report = Assert.IsType<ReviewReport>(result.Report);
        Assert.Equal(FindingAxis.CodingStandards, report.Axis);
        var finding = Assert.IsType<CodingStandardsFinding>(Assert.Single(report.Findings));
        Assert.Equal(12, finding.Line);
    }

    [Theory]
    [InlineData(AgentRole.ReviewerSpecification, """{ "axis": "specification", "verdict": "issues_found", "summary": "s", "findings": [] }""")]
    [InlineData(AgentRole.ReviewerSpecification, """{ "axis": "coding_standards", "verdict": "clean", "summary": "s", "findings": [] }""")]
    [InlineData(AgentRole.Implementer, """{ "status": "completed", "head_commit_sha": "not-a-sha", "summary": "s", "tests": [], "addressed_findings": [], "follow_ups": [] }""")]
    [InlineData(AgentRole.Implementer, """{ "status": "completed", "summary": "s" }""")]
    [InlineData(AgentRole.Explorer, """{ "status": "completed", "summary": "s", "notes_files": ["README.md"], "extra": 1 }""")]
    [InlineData(AgentRole.Tester, """{ "verdict": "maybe", "summary": "s", "scenarios": [], "issues": [] }""")]
    [InlineData(AgentRole.Tester, """[]""")]
    public void malformed_payloads_are_rejected_with_a_reason(AgentRole role, string payload)
    {
        ReportParseResult result = Parse(role, payload);

        Assert.Null(result.Report);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    private static ReportParseResult Parse(AgentRole role, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return AgentReportToolFactory.For(role).Parse(document.RootElement.Clone());
    }

    private static JsonElement Property(JsonElement schema, string name) => schema.GetProperty("properties").GetProperty(name);

    private static JsonElement Items(JsonElement arraySchema) => arraySchema.GetProperty("items");

    private static string[] PropertyNames(JsonElement schema) =>
        schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray();

    private static string[] RequiredNames(JsonElement schema) =>
        schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()!).ToArray();

    private static string[] EnumValues(JsonElement schema) =>
        schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString() ?? "null").ToArray();
}
