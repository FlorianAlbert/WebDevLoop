using WebDevLoop.Web.Api;
using WebDevLoop.Web.Background;
using WebDevLoop.Web.Components;

namespace WebDevLoop.Web.DependencyInjection;

public static class WebDevLoopApplicationExtensions
{
    /// <summary>Migrates and seeds the database, resolves the startup settings and evaluates the prerequisites.</summary>
    public static Task InitializeWebDevLoopAsync(this WebApplication app, CancellationToken cancellationToken = default) =>
        app.Services.GetRequiredService<IAppInitializer>().InitializeAsync(cancellationToken);

    /// <summary>The HTTP pipeline: error pages, static assets, antiforgery, the REST API with OpenAPI, and the Blazor UI.</summary>
    public static WebApplication UseWebDevLoop(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapWebDevLoopApi();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        return app;
    }
}
