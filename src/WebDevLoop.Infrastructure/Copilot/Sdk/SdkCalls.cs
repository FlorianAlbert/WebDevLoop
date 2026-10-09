using WebDevLoop.Infrastructure.Copilot.Runtime;

namespace WebDevLoop.Infrastructure.Copilot.Sdk;

/// <summary>Surfaces SDK authentication failures as <see cref="CopilotAuthenticationException"/>.</summary>
internal static class SdkCalls
{
    public static async Task RunAsync(Func<Task> call) => await RunAsync(async () =>
    {
        await call();
        return true;
    });

    public static async Task<T> RunAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (exception is not CopilotAuthenticationException && SdkFailures.IsAuthenticationFailure(exception))
        {
            throw new CopilotAuthenticationException(exception.Message, exception);
        }
    }
}
