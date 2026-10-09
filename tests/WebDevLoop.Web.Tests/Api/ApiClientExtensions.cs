using System.Net.Http.Json;
using System.Text.Json;

namespace WebDevLoop.Web.Tests.Api;

internal static class ApiClientExtensions
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> GetResponseAsync(this HttpClient client, string url) => client.GetAsync(url, Ct);

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object? body = null) =>
        client.PostAsJsonAsync(url, body, Ct);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body) => client.PutAsJsonAsync(url, body, Ct);

    public static Task<HttpResponseMessage> PatchJsonAsync(this HttpClient client, string url, object body) => client.PatchAsJsonAsync(url, body, Ct);

    public static Task<HttpResponseMessage> DeleteResponseAsync(this HttpClient client, string url) => client.DeleteAsync(url, Ct);

    public static Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string url)
    {
        using HttpResponseMessage response = await client.GetAsync(url, Ct);
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync();
    }
}
