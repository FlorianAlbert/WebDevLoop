using Microsoft.AspNetCore.Components;

namespace WebDevLoop.Web.Components.Dashboard;

/// <summary>
/// Runs every application-service call in its own DI scope. Blazor Server circuit scopes are long-lived and the EF
/// context behind the services is not thread-safe, so components on one page must not share a scope.
/// </summary>
public abstract class ScopedComponentBase : ComponentBase
{
    [Inject]
    private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    protected async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using AsyncServiceScope scope = ScopeFactory.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }
}
