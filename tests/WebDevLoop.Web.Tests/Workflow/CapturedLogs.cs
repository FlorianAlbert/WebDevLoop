using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace WebDevLoop.Web.Tests.Workflow;

/// <summary>Collects the app's warnings and errors so a failing scenario can show what went wrong inside the host.</summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                entries.Enqueue($"[{logLevel}] {category}: {formatter(state, exception)}{(exception is null ? string.Empty : Environment.NewLine + exception)}");
            }
        }
    }
}
