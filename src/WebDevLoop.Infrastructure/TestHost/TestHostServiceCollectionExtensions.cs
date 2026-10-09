using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebDevLoop.Core.Ports;

namespace WebDevLoop.Infrastructure.TestHost;

public static class TestHostServiceCollectionExtensions
{
    /// <summary>
    /// Registers the tester application supervisor (<see cref="ITestTargetRunner"/>) as a singleton, so every tester run of
    /// this process shares one set of port reservations.
    /// </summary>
    public static IServiceCollection AddTestHost(this IServiceCollection services, TestHostOptions? options = null)
    {
        services.TryAddSingleton(options ?? new TestHostOptions());
        services.TryAddSingleton<IPortProbe, LoopbackPortProbe>();
        services.TryAddSingleton<IProcessTable, ProcFileSystemProcessTable>();
        services.TryAddSingleton<IProcessTerminator, ProcessTerminator>();
        services.TryAddSingleton<ITestTargetRunner, TestTargetRunner>();
        return services;
    }
}
