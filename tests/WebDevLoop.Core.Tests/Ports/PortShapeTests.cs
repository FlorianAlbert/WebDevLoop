using System.Reflection;
using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Tests.Ports;

public sealed class PortShapeTests
{
    private const string AsyncSuffix = "Async";

    private static readonly Assembly Core = typeof(SpecRun).Assembly;

    public static TheoryData<string> AsyncPorts =>
    [
        "WebDevLoop.Core.Ports.IGitHubIssues",
        "WebDevLoop.Core.Ports.IGitHubPullsAndStacks",
        "WebDevLoop.Core.Ports.IGitWorkspace",
        "WebDevLoop.Core.Ports.IAgentRunner",
        "WebDevLoop.Core.Ports.IAgentLogSink",
        "WebDevLoop.Core.Ports.ICopilotRuntimePool",
        "WebDevLoop.Core.Ports.ITokenProvider",
        "WebDevLoop.Core.Ports.ITestTargetRunner",
        "WebDevLoop.Core.Ports.IPrerequisiteValidator",
        "WebDevLoop.Core.Ports.IUnitOfWork",
        "WebDevLoop.Core.Ports.IRepositoryRecordRepository",
        "WebDevLoop.Core.Ports.ISpecRunRepository",
        "WebDevLoop.Core.Ports.ITicketRunRepository",
        "WebDevLoop.Core.Ports.IStepRunRepository",
        "WebDevLoop.Core.Ports.IIntegrationSagaRepository",
        "WebDevLoop.Core.Ports.IPullStackLayerRepository",
        "WebDevLoop.Core.Ports.IFindingIssuanceRepository",
        "WebDevLoop.Core.Ports.ITestLeaseRepository",
        "WebDevLoop.Core.Ports.ISettingsProfileRepository",
        "WebDevLoop.Core.Ports.IRunEventRepository",
        "WebDevLoop.Core.Events.IOutbox",
        "WebDevLoop.Core.Events.IRunEventBus",
    ];

    public static TheoryData<string> SynchronousPorts =>
    [
        "WebDevLoop.Core.Ports.IClock",
        "WebDevLoop.Core.Ports.IIdGenerator",
    ];

    [Theory]
    [MemberData(nameof(AsyncPorts))]
    [MemberData(nameof(SynchronousPorts))]
    public void port_is_a_public_core_interface(string portName)
    {
        Type? port = Core.GetType(portName);

        Assert.NotNull(port);
        Assert.True(port.IsInterface && port.IsPublic, $"{portName} must be a public interface.");
    }

    [Theory]
    [MemberData(nameof(AsyncPorts))]
    public void async_port_methods_return_a_task_and_take_a_trailing_cancellation_token(string portName)
    {
        Type port = Core.GetType(portName) ?? throw new Xunit.Sdk.XunitException($"{portName} is missing.");
        MethodInfo[] asyncMethods = port.GetMethods().Where(method => IsAwaitable(method.ReturnType)).ToArray();

        Assert.NotEmpty(asyncMethods);
        Assert.All(port.GetMethods(), method => Assert.True(
            IsAwaitable(method.ReturnType) == method.Name.EndsWith(AsyncSuffix, StringComparison.Ordinal),
            $"{port.Name}.{method.Name}: only awaitable methods may (and must) end with '{AsyncSuffix}'."));
        Assert.All(asyncMethods, method => Assert.True(
            method.GetParameters().LastOrDefault()?.ParameterType == typeof(CancellationToken),
            $"{port.Name}.{method.Name} must end with a CancellationToken."));
    }

    [Fact]
    public void port_signatures_reference_only_core_and_base_library_types()
    {
        IEnumerable<Type> referenced = Core.GetTypes()
            .Where(type => type.IsInterface && type.IsPublic)
            .SelectMany(port => port.GetMethods())
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .SelectMany(Flatten);

        Assert.All(referenced, type => Assert.True(
            type.Assembly == Core || type.Namespace?.StartsWith(nameof(System), StringComparison.Ordinal) == true,
            $"Port signature references non-Core type {type.FullName}."));
    }

    private static bool IsAwaitable(Type type) =>
        type == typeof(Task) || type == typeof(ValueTask) ||
        (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)));

    private static IEnumerable<Type> Flatten(Type type)
    {
        Type unwrapped = type.IsByRef || type.IsArray ? type.GetElementType()! : type;
        yield return unwrapped;

        if (unwrapped.IsGenericType)
        {
            foreach (Type argument in unwrapped.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }
    }
}
