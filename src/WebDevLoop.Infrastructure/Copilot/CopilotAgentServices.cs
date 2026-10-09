using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Copilot.Sdk;
using WebDevLoop.Infrastructure.Skills;

namespace WebDevLoop.Infrastructure.Copilot;

/// <summary>
/// Composition entry point for the Copilot feature: the SDK-backed <see cref="IAgentRunner"/>, the shared
/// <see cref="ICopilotRuntimePool"/> it runs on, and the bundled skills catalog (also used by startup validation).
/// Register it as a singleton and dispose it on shutdown to stop the runtimes.
/// </summary>
public sealed class CopilotAgentServices : IAsyncDisposable
{
    private readonly CopilotRuntimePool _pool;

    private CopilotAgentServices(CopilotRuntimePool pool, IAgentRunner runner, BundledSkillsCatalog skills)
    {
        _pool = pool;
        Runner = runner;
        Skills = skills;
    }

    public IAgentRunner Runner { get; }

    public ICopilotRuntimePool RuntimePool => _pool;

    public BundledSkillsCatalog Skills { get; }

    public static CopilotAgentServices Create(
        ITokenProvider tokens,
        IAgentLogSink logSink,
        IClock clock,
        CopilotRuntimeOptions runtimeOptions,
        BundledSkillsOptions skillsOptions)
    {
        var pool = new CopilotRuntimePool(new SdkCopilotRuntimeFactory(), tokens, clock, runtimeOptions);
        var skills = new BundledSkillsCatalog(skillsOptions);
        return new CopilotAgentServices(pool, new CopilotAgentRunner(pool, skills, logSink, clock, runtimeOptions), skills);
    }

    public ValueTask DisposeAsync() => _pool.DisposeAsync();
}
