using Microsoft.Extensions.DependencyInjection.Extensions;
using WebDevLoop.Web.Components.Repositories;

namespace WebDevLoop.Web.DependencyInjection;

public static class WebUiServiceCollectionExtensions
{
    /// <summary>Registers the services of the dashboard, repository and queue pages. Needs <c>ICurrentRepositorySelection</c> (see <see cref="WebApiServiceCollectionExtensions.AddWebDevLoopApi"/>).</summary>
    public static IServiceCollection AddWebDevLoopDashboardUi(this IServiceCollection services)
    {
        services.TryAddSingleton<RepositoryContext>();
        return services;
    }
}
