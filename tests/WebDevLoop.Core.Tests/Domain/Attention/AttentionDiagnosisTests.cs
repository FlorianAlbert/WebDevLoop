using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Domain.Attention;

public sealed class AttentionDiagnosisTests
{
    private static readonly AttentionDiagnosis Diagnosis = new("Troubleshooter", "The database is missing.", ["Ran the tests", "Checked the service"]);

    [Fact]
    public void a_diagnosis_adds_the_agents_actions_steps_and_buttons_to_the_reason()
    {
        AttentionReason reason = AttentionReasons.ImplementerBlocked("no database");

        AttentionReason diagnosed = reason.WithDiagnosis(Diagnosis, ["Install PostgreSQL."], [AttentionActionKind.Skip]);

        Assert.Equal(Diagnosis, diagnosed.Diagnosis);
        Assert.Contains("Troubleshooter: Checked the service", diagnosed.TriedSoFar);
        Assert.Equal("Install PostgreSQL.", diagnosed.UserSteps[0].Text);
        Assert.Equal(reason.UserSteps.Count + 1, diagnosed.UserSteps.Count);
        Assert.Equal(AttentionActionKind.Skip, diagnosed.PrimaryAction.Kind);
        Assert.Equal(reason.Actions.Count, diagnosed.Actions.Count);
        Assert.Equal(reason.Summary, diagnosed.Summary);
    }

    [Fact]
    public void a_suggested_button_the_catalogue_does_not_offer_is_ignored()
    {
        AttentionReason reason = AttentionReasons.ImplementerBlocked("no database");
        AttentionActionKind[] offered = [.. reason.Actions.Select(action => action.Kind)];
        AttentionActionKind notOffered = Enum.GetValues<AttentionActionKind>().FirstOrDefault(kind => !offered.Contains(kind), (AttentionActionKind)99);

        AttentionReason diagnosed = reason.WithDiagnosis(Diagnosis, [], [notOffered]);

        Assert.Equal(offered, diagnosed.Actions.Select(action => action.Kind));
    }

    [Fact]
    public void the_diagnosis_survives_json_and_older_json_without_one_still_loads()
    {
        AttentionReason diagnosed = AttentionReasons.WorktreeNotClean("/w", "b", "dirty").WithDiagnosis(Diagnosis, ["Look."], []);

        AttentionReason? parsed = AttentionReason.TryParse(diagnosed.ToJson());
        AttentionReason? old = AttentionReason.TryParse(AttentionReasons.WorktreeNotClean("/w", "b", "dirty").ToJson().Replace(",\"diagnosis\":null", string.Empty, StringComparison.Ordinal));

        Assert.Equal(Diagnosis.Summary, parsed!.Diagnosis!.Summary);
        Assert.Equal(Diagnosis.ActionsTaken, parsed.Diagnosis.ActionsTaken);
        Assert.Null(old!.Diagnosis);
    }

    [Fact]
    public void the_other_with_methods_keep_the_diagnosis()
    {
        AttentionReason diagnosed = AttentionReasons.ImplementerBlocked("x").WithDiagnosis(Diagnosis, [], []);

        Assert.Equal(Diagnosis, diagnosed.WithTried("more").Diagnosis);
        Assert.Equal(Diagnosis, diagnosed.WithDetails("details").Diagnosis);
        Assert.Equal(Diagnosis, diagnosed.WithAutoFixAttempted("failed").Diagnosis);
    }
}
