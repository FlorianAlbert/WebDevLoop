using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.Components.Health;

namespace WebDevLoop.Web.Tests.Components.Health;

public sealed class HealthPageTests : BunitContext
{
    private static readonly PrerequisiteCheck MissingSkill = new(
        "Bundled skills", PrerequisiteStatus.Failed, "Skill 'tdd' is missing from the output folder.", "Rebuild so the bundled skills are copied to the output directory.");

    private static readonly PrerequisiteCheck GitOk = new("Git CLI", PrerequisiteStatus.Passed, "git 2.50 found.");

    private static readonly PrerequisiteCheck GhStackWarning = new("gh stack", PrerequisiteStatus.Warning, "gh stack is not installed.", "Install the gh-stack extension.");

    private readonly ScriptedValidator _validator = new();

    private async Task<IRenderedComponent<HealthPage>> RenderAsync(params PrerequisiteCheck[] checks)
    {
        _validator.Checks = checks;
        var readiness = new DiagnosticReadiness(_validator, new FixedClock());
        if (checks.Length > 0)
        {
            await readiness.RefreshAsync(CancellationToken.None);
        }

        Services.AddSingleton(readiness);
        return Render<HealthPage>();
    }

    [Fact]
    public async Task missing_skill_and_disabled_mutating_workflow_message_are_rendered()
    {
        IRenderedComponent<HealthPage> page = await RenderAsync(GitOk, MissingSkill);

        Assert.Equal("Diagnostic-only", page.Find("[data-testid=readiness-mode]").TextContent.Trim());
        Assert.Contains("Mutating workflow", page.Find("[data-testid=workflow-disabled]").TextContent);
        AssertCheckFailed(page, "Bundled skills");
        Assert.Contains("Skill 'tdd' is missing", page.Markup);
        Assert.Contains("Rebuild so the bundled skills are copied", page.Markup);
    }

    [Fact]
    public async Task operational_mode_shows_no_disabled_message_and_lists_passed_checks()
    {
        IRenderedComponent<HealthPage> page = await RenderAsync(GitOk, GhStackWarning);

        Assert.Equal("Operational", page.Find("[data-testid=readiness-mode]").TextContent.Trim());
        Assert.Empty(page.FindAll("[data-testid=workflow-disabled]"));
        Assert.Equal(["Warning", "Passed"], page.FindAll("[data-testid=check]").Select(row => row.GetAttribute("data-status")!).ToArray());
    }

    [Fact]
    public async Task before_the_first_evaluation_the_page_says_so_and_stays_diagnostic_only()
    {
        IRenderedComponent<HealthPage> page = await RenderAsync();

        Assert.Equal("Diagnostic-only", page.Find("[data-testid=readiness-mode]").TextContent.Trim());
        Assert.Contains("not been evaluated", page.Find("[data-testid=not-evaluated]").TextContent);
    }

    [Fact]
    public async Task rechecking_runs_the_validator_again_and_shows_the_new_result()
    {
        IRenderedComponent<HealthPage> page = await RenderAsync(MissingSkill);
        _validator.Checks = [GitOk];

        await page.Find("[data-testid=recheck]").ClickAsync();

        page.WaitForAssertion(() => Assert.Equal("Operational", page.Find("[data-testid=readiness-mode]").TextContent.Trim()));
        Assert.Equal(2, _validator.Calls);
    }

    private static void AssertCheckFailed(IRenderedComponent<HealthPage> page, string name)
    {
        AngleSharp.Dom.IElement row = page.FindAll("[data-testid=check]").Single(candidate => candidate.TextContent.Contains(name));
        Assert.Equal("Failed", row.GetAttribute("data-status"));
    }

    private sealed class ScriptedValidator : IPrerequisiteValidator
    {
        public PrerequisiteCheck[] Checks { get; set; } = [];

        public int Calls { get; private set; }

        public Task<PrerequisiteReport> ValidateAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new PrerequisiteReport(Checks));
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    }
}
