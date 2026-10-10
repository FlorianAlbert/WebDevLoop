using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Infrastructure.Persistence;

internal sealed class RunIdConverter() : ValueConverter<RunId, string>(id => id.Value, value => new RunId(value));

internal sealed class TicketRunIdConverter() : ValueConverter<TicketRunId, string>(id => id.Value, value => new TicketRunId(value));

internal sealed class StepRunIdConverter() : ValueConverter<StepRunId, string>(id => id.Value, value => new StepRunId(value));

internal sealed class BranchNameConverter() : ValueConverter<BranchName, string>(name => name.Value, value => new BranchName(value));

internal sealed class CommitShaConverter() : ValueConverter<CommitSha, string>(sha => sha.Value, value => new CommitSha(value));

internal sealed class FindingFingerprintConverter() : ValueConverter<FindingFingerprint, string>(
    fingerprint => fingerprint.Value,
    value => new FindingFingerprint(value));

internal sealed class PullRequestNumberConverter() : ValueConverter<PullRequestNumber, int>(
    number => number.Value,
    value => new PullRequestNumber(value));

/// <summary>Structured attention guidance, stored as JSON; unreadable JSON reads back as no guidance.</summary>
internal sealed class AttentionReasonConverter() : ValueConverter<AttentionReason, string>(
    reason => reason.ToJson(),
    json => AttentionReason.TryParse(json)!);

/// <summary>Stored as UTC ticks so SQLite can order and compare timestamps; offsets are normalised to UTC.</summary>
internal sealed class DateTimeOffsetTicksConverter() : ValueConverter<DateTimeOffset, long>(
    value => value.UtcTicks,
    ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

internal static class ValueConverterConventions
{
    public static void Apply(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<RunId>().HaveConversion<RunIdConverter>();
        configurationBuilder.Properties<TicketRunId>().HaveConversion<TicketRunIdConverter>();
        configurationBuilder.Properties<StepRunId>().HaveConversion<StepRunIdConverter>();
        configurationBuilder.Properties<BranchName>().HaveConversion<BranchNameConverter>();
        configurationBuilder.Properties<CommitSha>().HaveConversion<CommitShaConverter>();
        configurationBuilder.Properties<FindingFingerprint>().HaveConversion<FindingFingerprintConverter>();
        configurationBuilder.Properties<PullRequestNumber>().HaveConversion<PullRequestNumberConverter>();
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetTicksConverter>();
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }
}
