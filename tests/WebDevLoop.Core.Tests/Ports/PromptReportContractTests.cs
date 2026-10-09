using System.Reflection;
using System.Text.RegularExpressions;
using WebDevLoop.Core.Orchestration.Results;

namespace WebDevLoop.Core.Tests.Ports;

/// <summary>
/// The shipped prompt templates tell agents which report-tool fields to send. Every field (snake_case) must exist as a
/// PascalCase property on the matching Core contract, and every enum value the template offers must exist on that enum.
/// </summary>
public sealed partial class PromptReportContractTests
{
    private const string PromptDirectory = "src/WebDevLoop.Web/Resources/Prompts";
    private const string ReportSection = "## Report";
    private const string FindingSectionPrefix = "## What counts as a";

    public static TheoryData<string, string, string?> Templates => new()
    {
        { "Explorer.md", "ExplorationReport", null },
        { "Implementer.md", "ImplementationReport", null },
        { "ReviewerCodingStandards.md", "ReviewReport", "CodingStandardsFinding" },
        { "ReviewerSpecification.md", "ReviewReport", "SpecificationFinding" },
        { "ConflictResolver.md", "ConflictResolutionReport", null },
        { "Tester.md", "TestReport", "TestIssue" },
    };

    [Theory]
    [MemberData(nameof(Templates))]
    public void report_fields_requested_by_the_template_exist_on_the_contract(string template, string reportType, string? findingType)
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), PromptDirectory, template));

        AssertSectionMatches(template, Section(lines, ReportSection), ResultType(reportType));
        if (findingType is not null)
        {
            AssertSectionMatches(template, Section(lines, FindingSectionPrefix), ResultType(findingType));
        }
    }

    private static void AssertSectionMatches(string template, IReadOnlyList<string> bullets, Type? contract)
    {
        Assert.True(contract is not null, $"{template}: contract type is missing.");
        Assert.NotEmpty(bullets);
        string[] fieldNames = bullets.SelectMany(bullet => FieldNames(bullet)).ToArray();

        foreach (string bullet in bullets)
        {
            string[] fields = FieldNames(bullet).ToArray();
            foreach (string field in fields)
            {
                PropertyInfo? property = contract.GetProperty(Pascal(field));
                Assert.True(property is not null, $"{template}: '{field}' has no property {contract.Name}.{Pascal(field)}.");

                Type? enumType = EnumType(property.PropertyType);
                if (fields.Length == 1 && enumType is not null)
                {
                    foreach (string value in EnumValues(bullet).Except(fieldNames))
                    {
                        Assert.True(Enum.GetNames(enumType).Contains(Pascal(value)), $"{template}: '{field}' value '{value}' is missing on {enumType.Name}.");
                    }
                }
            }
        }
    }

    private static List<string> Section(string[] lines, string headingPrefix)
    {
        IEnumerable<string> section = lines
            .SkipWhile(line => !line.StartsWith(headingPrefix, StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal));
        return section.Where(line => line.StartsWith("- `", StringComparison.Ordinal)).ToList();
    }

    private static IEnumerable<string> FieldNames(string bullet)
    {
        int colon = bullet.IndexOf("`:", StringComparison.Ordinal);
        string head = colon < 0 ? bullet : bullet[..(colon + 1)];
        return Identifiers(head);
    }

    private static IEnumerable<string> EnumValues(string bullet)
    {
        int colon = bullet.IndexOf("`:", StringComparison.Ordinal);
        return colon < 0 ? [] : Identifiers(bullet[(colon + 2)..]);
    }

    private static IEnumerable<string> Identifiers(string text) =>
        Identifier().Matches(text).Select(match => match.Groups[1].Value);

    private static Type? EnumType(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsEnum ? underlying : null;
    }

    private static Type? ResultType(string name) => typeof(AgentReport).Assembly.GetType($"{typeof(AgentReport).Namespace}.{name}");

    private static string Pascal(string snake) =>
        string.Concat(snake.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WebDevLoop.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    [GeneratedRegex("`([a-z][a-z_]*)`")]
    private static partial Regex Identifier();
}
