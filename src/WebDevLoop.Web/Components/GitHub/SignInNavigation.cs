using Microsoft.AspNetCore.Components;
using WebDevLoop.Web.GitHubAuth;

namespace WebDevLoop.Web.Components.GitHub;

internal static class SignInNavigation
{
    /// <summary>Leaves the Blazor circuit for the sign-in endpoint (a full page load), returning to the current page afterwards.</summary>
    public static void SignInWithGitHub(this NavigationManager navigation)
    {
        string returnUrl = "/" + navigation.ToBaseRelativePath(navigation.Uri).Split('?', '#')[0];
        navigation.NavigateTo(GitHubSignInEndpoints.LoginUrl(returnUrl), forceLoad: true);
    }
}
