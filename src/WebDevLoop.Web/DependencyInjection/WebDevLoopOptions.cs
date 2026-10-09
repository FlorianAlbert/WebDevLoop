using Microsoft.Data.Sqlite;
using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Infrastructure.Queries;

namespace WebDevLoop.Web.DependencyInjection;

/// <summary>
/// App configuration (section <c>WebDevLoop</c>; override any key with environment variables such as
/// <c>WebDevLoop__GitHub__AppClientId</c>). Workflow behaviour that users tune at runtime (limits, prompts, workspace root,
/// test port range) lives in the persisted settings instead; secrets never go into appsettings.json.
/// </summary>
public sealed class WebDevLoopOptions
{
    public const string SectionName = "WebDevLoop";
    public const string DatabaseFileName = "webdevloop.db";
    public const string UiStateFileName = "ui-state.json";
    public const string GitHubCredentialsFileName = "github-credentials.dat";
    public const string DataProtectionKeysDirectoryName = "keys";
    private const string AppDirectoryName = "WebDevLoop";

    /// <summary>App data root: database, default workspace root and Copilot home. Defaults to the user's local app data.</summary>
    public string? DataDirectory { get; set; }

    /// <summary>SQLite database file; defaults to <c>webdevloop.db</c> in <see cref="DataDirectory"/>.</summary>
    public string? DatabasePath { get; set; }

    public WorkflowWorkerOptions Workflow { get; set; } = new();

    public GitHubConnectionOptions GitHub { get; set; } = new();

    public CopilotConnectionOptions Copilot { get; set; } = new();

    public ToolPathOptions Tools { get; set; } = new();

    /// <summary>Persisted live agent logs (section <c>WebDevLoop:AgentLogs</c>): retention per step, batching, page size.</summary>
    public AgentLogStoreOptions AgentLogs { get; set; } = new();

    public string ResolvedDataDirectory => Path.GetFullPath(string.IsNullOrWhiteSpace(DataDirectory) ? DefaultDataDirectory() : DataDirectory);

    public string ResolvedDatabasePath => Path.GetFullPath(string.IsNullOrWhiteSpace(DatabasePath) ? Path.Combine(ResolvedDataDirectory, DatabaseFileName) : DatabasePath);

    public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = ResolvedDatabasePath }.ToString();

    public string UiStatePath => Path.Combine(ResolvedDataDirectory, UiStateFileName);

    /// <summary>The signed-in user's GitHub tokens, encrypted with the data-protection keys in <see cref="DataProtectionKeysDirectory"/>.</summary>
    public string GitHubCredentialsPath => Path.Combine(ResolvedDataDirectory, GitHubCredentialsFileName);

    public string DataProtectionKeysDirectory => Path.Combine(ResolvedDataDirectory, DataProtectionKeysDirectoryName);

    /// <summary>Binds and validates the section.</summary>
    /// <exception cref="WebDevLoopConfigurationException">The configuration is invalid; the message lists every problem.</exception>
    public static WebDevLoopOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        WebDevLoopOptions options = configuration.GetSection(SectionName).Get<WebDevLoopOptions>() ?? new WebDevLoopOptions();
        List<string> errors = [.. options.Validate()];
        return errors.Count == 0 ? options : throw new WebDevLoopConfigurationException(errors);
    }

    private IEnumerable<string> Validate()
    {
        foreach ((string name, TimeSpan value) in Workflow.Intervals())
        {
            if (value <= TimeSpan.Zero)
            {
                yield return $"{SectionName}:Workflow:{name} must be a positive time span (e.g. \"00:01:00\"), but is {value}.";
            }
        }

        foreach ((string name, string? url) in new[] { ("ApiBaseUrl", GitHub.ApiBaseUrl), ("GraphQlUrl", GitHub.GraphQlUrl), ("WebBaseUrl", GitHub.WebBaseUrl) })
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                yield return $"{SectionName}:GitHub:{name} must be an absolute URL, but is '{url}'.";
            }
        }
    }

    private static string DefaultDataDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), AppDirectoryName);
}

/// <summary>Hosted workflow workers (section <c>WebDevLoop:Workflow</c>).</summary>
public sealed class WorkflowWorkerOptions
{
    /// <summary>False runs the UI/API only: no schedulers, recovery, or event handlers (e.g. a read-only dashboard).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether specs are explored by the explorer agent before their tickets are dispatched.</summary>
    public bool ExplorationEnabled { get; set; } = true;

    /// <summary>Delay of the outbox worker after a pass that found nothing to dispatch.</summary>
    public TimeSpan OutboxPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How often startup recovery is retried while prerequisites are unhealthy (diagnostic-only mode).</summary>
    public TimeSpan StartupRetryInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Periodic reconciliation: external state, stalled agent steps, repo queues, and frontiers.</summary>
    public TimeSpan RecoveryInterval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How often ready PR stacks are polled for the human merge.</summary>
    public TimeSpan MergeTrackingInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How often idle Copilot runtimes are stopped and runtimes with expiring tokens replaced.</summary>
    public TimeSpan RuntimeMaintenanceInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Dispatched outbox messages older than this are deleted.</summary>
    public TimeSpan OutboxRetention { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan OutboxPurgeInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>A working ticket/spec idle this long without an active step is relaunched by recovery.</summary>
    public TimeSpan StallGracePeriod { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>A saga parked in NeedsAttention after moving the integration branch is retried after this long.</summary>
    public TimeSpan ParkedIntegrationRetryInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long trunk may lack the top layer of a merged stack before the spec needs attention.</summary>
    public TimeSpan TrunkContainmentTimeout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long the tester's app may take to listen on the reserved port.</summary>
    public TimeSpan TesterAppStartupTimeout { get; set; } = TimeSpan.FromMinutes(10);

    internal IEnumerable<(string Name, TimeSpan Value)> Intervals() =>
    [
        (nameof(OutboxPollInterval), OutboxPollInterval),
        (nameof(StartupRetryInterval), StartupRetryInterval),
        (nameof(RecoveryInterval), RecoveryInterval),
        (nameof(MergeTrackingInterval), MergeTrackingInterval),
        (nameof(RuntimeMaintenanceInterval), RuntimeMaintenanceInterval),
        (nameof(OutboxRetention), OutboxRetention),
        (nameof(OutboxPurgeInterval), OutboxPurgeInterval),
        (nameof(StallGracePeriod), StallGracePeriod),
        (nameof(ParkedIntegrationRetryInterval), ParkedIntegrationRetryInterval),
        (nameof(TrunkContainmentTimeout), TrunkContainmentTimeout),
        (nameof(TesterAppStartupTimeout), TesterAppStartupTimeout),
    ];
}

/// <summary>GitHub endpoints and the GitHub App users sign in with (section <c>WebDevLoop:GitHub</c>).</summary>
public sealed class GitHubConnectionOptions
{
    public string ApiBaseUrl { get; set; } = GitHubAuthOptions.DefaultApiBaseUrl;

    public string GraphQlUrl { get; set; } = "https://api.github.com/graphql";

    /// <summary>GitHub's web origin, where users authorize the App.</summary>
    public string WebBaseUrl { get; set; } = GitHubAuthOptions.DefaultWebBaseUrl;

    /// <summary>The GitHub App's client id (<c>Iv23…</c>).</summary>
    public string? AppClientId { get; set; }

    /// <summary>A client secret of the GitHub App (secret: user secrets or environment variables only).</summary>
    public string? AppClientSecret { get; set; }

    /// <summary>The App's URL name (<c>github.com/apps/&lt;slug&gt;</c>); optional, enables the "Install the App" link before the first installation.</summary>
    public string? AppSlug { get; set; }

    public GhStackMode GhStackMode { get; set; } = GhStackMode.RestWithOptionalFallback;

    public string GhExecutable { get; set; } = PrerequisiteOptions.DefaultGhExecutable;
}

/// <summary>Copilot runtime (section <c>WebDevLoop:Copilot</c>).</summary>
public sealed class CopilotConnectionOptions
{
    /// <summary>Copilot CLI to launch; empty uses the runtime bundled into the published app.</summary>
    public string? CliPath { get; set; }

    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>External command-line tools (section <c>WebDevLoop:Tools</c>).</summary>
public sealed class ToolPathOptions
{
    public string GitExecutable { get; set; } = PrerequisiteOptions.DefaultGitExecutable;

    public string PlaywrightCliExecutable { get; set; } = PrerequisiteOptions.DefaultPlaywrightCliExecutable;
}

/// <summary>The app cannot start with this configuration (fail fast at startup with every problem listed).</summary>
public sealed class WebDevLoopConfigurationException(IReadOnlyList<string> errors)
    : Exception("Invalid WebDevLoop configuration:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(error => " - " + error)))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
