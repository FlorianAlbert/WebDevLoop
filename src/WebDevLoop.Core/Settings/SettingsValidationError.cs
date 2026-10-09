namespace WebDevLoop.Core.Settings;

/// <summary>A single settings validation problem; <see cref="Field"/> names the offending setting.</summary>
public sealed record SettingsValidationError(string Field, string Message)
{
    public override string ToString() => $"{Field}: {Message}";
}
