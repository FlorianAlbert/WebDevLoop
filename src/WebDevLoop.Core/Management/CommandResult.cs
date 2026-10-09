using WebDevLoop.Core.Settings;

namespace WebDevLoop.Core.Management;

public sealed record CommandResult<T>(CommandStatus Status, T? Value = default, IReadOnlyList<SettingsValidationError>? Errors = null, string? Message = null)
{
    public bool IsSuccess => Status == CommandStatus.Succeeded;

    public static CommandResult<T> Succeeded(T value) => new(CommandStatus.Succeeded, value);

    public static CommandResult<T> NotFound(string message) => new(CommandStatus.NotFound, Message: message);

    public static CommandResult<T> Invalid(IReadOnlyList<SettingsValidationError> errors) =>
        new(CommandStatus.Invalid, Errors: errors, Message: string.Join("; ", errors));

    public static CommandResult<T> Conflict(string message) => new(CommandStatus.Conflict, Message: message);
}
