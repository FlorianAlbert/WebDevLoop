using Bunit;
using WebDevLoop.Web.Components.Pages;

namespace WebDevLoop.Web.Tests.Components.Pages;

public sealed class ErrorAndNotFoundTests : BunitContext
{
    [Fact]
    public void Error_page_is_friendly_and_keeps_the_request_id_in_a_disclosure()
    {
        IRenderedComponent<Error> cut = Render<Error>(p => p.AddCascadingValue(new Microsoft.AspNetCore.Http.DefaultHttpContext { TraceIdentifier = "req-123" }));

        Assert.Equal("Something went wrong", cut.Find("h1").TextContent.Trim());
        Assert.Equal("/", cut.Find("a.btn-primary").GetAttribute("href"));
        Assert.Contains("req-123", cut.Find("details").TextContent);
        Assert.DoesNotContain("Development", cut.Markup);
    }

    [Fact]
    public void Not_found_page_links_back()
    {
        IRenderedComponent<NotFound> cut = Render<NotFound>();

        Assert.Single(cut.FindAll("h1"));
        Assert.Equal(["/", "/queue"], cut.FindAll("a.btn").Select(a => a.GetAttribute("href")!).ToArray());
    }
}
