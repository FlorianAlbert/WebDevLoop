using System.Text.Json;

namespace WebDevLoop.Web.Components.Steps;

/// <summary>Reads the snake_case structured result persisted with a step. Unknown shapes still pretty-print; only known fields are lifted out.</summary>
public static class StepReportParser
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static StepReport? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string pretty = JsonSerializer.Serialize(root, Indented);
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new StepReport(null, null, null, [], pretty);
            }

            return new StepReport(Text(root, "summary"), Text(root, "verdict"), Number(root, "iteration"), ReadFindings(root), pretty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<ReportFinding> ReadFindings(JsonElement root)
    {
        if (!root.TryGetProperty("findings", out JsonElement findings) || findings.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return findings.EnumerateArray()
            .Where(finding => finding.ValueKind == JsonValueKind.Object)
            .Select(finding => new ReportFinding(
                Text(finding, "title") ?? Text(finding, "description") ?? "Finding",
                Text(finding, "severity") ?? Text(finding, "kind"),
                Text(finding, "file"),
                Number(finding, "line"),
                Text(finding, "description"),
                Text(finding, "recommendation")))
            .ToList();
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number) ? number : null;
}
