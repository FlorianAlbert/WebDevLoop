using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Infrastructure.GitHub.Auth;
using WebDevLoop.Web.Components.Layout;
using Microsoft.AspNetCore.Components;
using WebDevLoop.Web.Tests.Components.Dashboard;

namespace WebDevLoop.Web.Tests.Components.Layout;

public sealed class MainLayoutErrorUiTests : UiTestContext
{
    private sealed class SignedInState : IGitHubSignInState
    {
        public GitHubSignInStatus Status { get; } = new(true, "octo", null, null);

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }

    [Fact]
    public void error_bar_is_a_labelled_alert_with_reload_and_a_dismiss_button()
    {
        Services.AddSingleton<IGitHubSignInState>(new SignedInState());

        IRenderedComponent<MainLayout> cut = Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => { })));

        var bar = cut.Find("#blazor-error-ui");

        Assert.Equal("alert", bar.GetAttribute("role"));
        Assert.Equal(".", cut.Find("#blazor-error-ui a.reload").GetAttribute("href"));
        Assert.Equal("Dismiss", cut.Find("#blazor-error-ui button.dismiss").GetAttribute("aria-label"));
        Assert.Contains("Something went wrong", bar.TextContent);
    }
}
