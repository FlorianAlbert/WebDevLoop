namespace WebDevLoop.Core.Settings;

public sealed class SettingsValidationException(IReadOnlyList<SettingsValidationError> errors)
    : Exception("Invalid settings: " + string.Join("; ", errors))
{
    public IReadOnlyList<SettingsValidationError> Errors { get; } = errors;
}
