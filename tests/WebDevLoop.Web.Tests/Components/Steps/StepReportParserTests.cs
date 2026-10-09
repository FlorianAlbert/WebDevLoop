using WebDevLoop.Web.Components.Steps;

namespace WebDevLoop.Web.Tests.Components.Steps;

public sealed class StepReportParserTests
{
    private const string ReviewJson = """
        {"attempt":1,"iteration":2,"reviewed_head":"abc","axis":"coding_standards","verdict":"issues_found","summary":"Two problems",
         "findings":[
           {"axis":"coding_standards","title":"Magic number","file":"A.cs","line":12,"description":"Uses 42","recommendation":"Name it","severity":"major","rule":"R1"},
           {"axis":"specification","title":"Missing case","file":"B.cs","description":"No empty input","recommendation":"Handle it","kind":"missing"}]}
        """;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    public void Missing_or_malformed_json_yields_no_report(string? json) => Assert.Null(StepReportParser.TryParse(json));

    [Fact]
    public void Review_record_exposes_summary_verdict_iteration()
    {
        StepReport report = StepReportParser.TryParse(ReviewJson)!;

        Assert.Equal("Two problems", report.Summary);
        Assert.Equal("issues_found", report.Verdict);
        Assert.Equal(2, report.Iteration);
    }

    [Fact]
    public void Findings_are_listed_with_location_and_category()
    {
        StepReport report = StepReportParser.TryParse(ReviewJson)!;

        Assert.Equal(2, report.Findings.Count);
        Assert.Equal(new ReportFinding("Magic number", "major", "A.cs", 12, "Uses 42", "Name it"), report.Findings[0]);
        Assert.Equal(new ReportFinding("Missing case", "missing", "B.cs", null, "No empty input", "Handle it"), report.Findings[1]);
    }

    [Fact]
    public void Report_without_findings_still_pretty_prints_the_payload()
    {
        StepReport report = StepReportParser.TryParse("""{"status":"completed","summary":"Done"}""")!;

        Assert.Empty(report.Findings);
        Assert.Equal("Done", report.Summary);
        Assert.Contains("\n", report.PrettyJson);
        Assert.Contains("\"status\": \"completed\"", report.PrettyJson);
    }

    [Fact]
    public void Non_object_payload_is_shown_verbatim_without_fields()
    {
        StepReport report = StepReportParser.TryParse("[1,2]")!;

        Assert.Null(report.Summary);
        Assert.Empty(report.Findings);
    }
}
