using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Prerequisites;

namespace WebDevLoop.Infrastructure.Tests.Prerequisites;

public sealed class StorageChecksTests
{
    [Fact]
    public async Task writable_workspace_root_passes()
    {
        var fileSystem = new FakeFileSystemProbe();

        PrerequisiteCheck result = await new WorkspaceRootCheck(TestPrerequisiteOptions.Create(), fileSystem).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Passed, result.Status);
        Assert.Equal([TestPrerequisiteOptions.WorkspaceRoot], fileSystem.WritableChecks);
    }

    [Fact]
    public async Task unwritable_workspace_root_fails_with_the_path_and_reason()
    {
        var fileSystem = new FakeFileSystemProbe();
        fileSystem.UnwritableDirectories[TestPrerequisiteOptions.WorkspaceRoot] = "Access denied";

        PrerequisiteCheck result = await new WorkspaceRootCheck(TestPrerequisiteOptions.Create(), fileSystem).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains(TestPrerequisiteOptions.WorkspaceRoot, result.Message);
        Assert.Contains("Access denied", result.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Remediation));
    }

    [Fact]
    public async Task migrated_database_passes()
    {
        var probe = new FakeDatabaseProbe(() => new DatabaseState(CanConnect: true, PendingMigrations: []));

        PrerequisiteCheck result = await new DatabaseCheck(probe).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Passed, result.Status);
    }

    [Fact]
    public async Task unreachable_database_fails()
    {
        var probe = new FakeDatabaseProbe(() => new DatabaseState(CanConnect: false, PendingMigrations: []));

        PrerequisiteCheck result = await new DatabaseCheck(probe).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
    }

    [Fact]
    public async Task pending_migrations_fail_and_are_listed()
    {
        var probe = new FakeDatabaseProbe(() => new DatabaseState(CanConnect: true, PendingMigrations: ["20260101_Init", "20260201_Outbox"]));

        PrerequisiteCheck result = await new DatabaseCheck(probe).RunAsync(CancellationToken.None);

        Assert.Equal(PrerequisiteStatus.Failed, result.Status);
        Assert.Contains("20260101_Init", result.Message);
        Assert.Contains("20260201_Outbox", result.Message);
    }
}
