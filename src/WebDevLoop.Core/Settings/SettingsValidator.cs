using WebDevLoop.Core.Domain;

namespace WebDevLoop.Core.Settings;

/// <summary>Validates one settings profile before it is persisted. Unset (<c>null</c>) values are always valid.</summary>
public static class SettingsValidator
{
    public const int MinimumUnprivilegedPort = 1024;

    private const int MinimumPositiveValue = 1;
    private const int MinimumRetries = 0;

    public static IReadOnlyList<SettingsValidationError> Validate(SettingsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = new List<SettingsValidationError>();

        RequireAtLeast(errors, nameof(profile.MaxActiveSpecsPerRepo), profile.MaxActiveSpecsPerRepo, MinimumPositiveValue);
        ValidateGlobalImplementerLimit(errors, profile);
        RequireGlobalOnly(errors, profile, nameof(profile.WorkspaceRootDirectory), profile.WorkspaceRootDirectory);
        RequireGlobalOnly(errors, profile, nameof(profile.CopilotBaseDirectory), profile.CopilotBaseDirectory);
        RequireAtLeast(errors, nameof(profile.MaxConcurrentImplementersPerRepo), profile.MaxConcurrentImplementersPerRepo, MinimumPositiveValue);
        RequireAtLeast(errors, nameof(profile.MaxReviewIterations), profile.MaxReviewIterations, MinimumPositiveValue);
        RequireAtLeast(errors, nameof(profile.MaxRetries), profile.MaxRetries, MinimumRetries);
        RequireAtLeast(errors, nameof(profile.ParentReviewCycleLimit), profile.ParentReviewCycleLimit, MinimumPositiveValue);
        RequireAtLeast(errors, nameof(profile.TesterCycleLimit), profile.TesterCycleLimit, MinimumPositiveValue);
        RequireAtLeast(errors, nameof(profile.TroubleshooterMaxAttempts), profile.TroubleshooterMaxAttempts, MinimumPositiveValue);
        ValidatePortRange(errors, profile.TestPortRange);

        foreach ((AgentRole role, RoleSettingsOverride roleSettings) in profile.Roles.OrderBy(entry => entry.Key))
        {
            ValidateRole(errors, role, roleSettings);
        }

        return errors;
    }

    /// <exception cref="SettingsValidationException">The profile has at least one validation error.</exception>
    public static void EnsureValid(SettingsProfile profile)
    {
        IReadOnlyList<SettingsValidationError> errors = Validate(profile);
        if (errors.Count > 0)
        {
            throw new SettingsValidationException(errors);
        }
    }

    private static void ValidateGlobalImplementerLimit(List<SettingsValidationError> errors, SettingsProfile profile)
    {
        const string field = nameof(profile.MaxConcurrentImplementersGlobal);
        if (!profile.IsGlobal && profile.MaxConcurrentImplementersGlobal is not null)
        {
            errors.Add(new SettingsValidationError(field, "The global implementer limit can only be set in global settings."));
            return;
        }

        RequireAtLeast(errors, field, profile.MaxConcurrentImplementersGlobal, MinimumPositiveValue);
    }

    /// <summary>The workspace root and the Copilot home configure process-wide resources built once at startup, so only the global layer may set them.</summary>
    private static void RequireGlobalOnly(List<SettingsValidationError> errors, SettingsProfile profile, string field, string? value)
    {
        if (!profile.IsGlobal && value is not null)
        {
            errors.Add(new SettingsValidationError(field, "Can only be set in global settings; it is read once at startup and shared by all repositories."));
        }
    }

    private static void ValidatePortRange(List<SettingsValidationError> errors, TestPortRange? range)
    {
        if (range is { } value && value.Start < MinimumUnprivilegedPort)
        {
            errors.Add(new SettingsValidationError(
                nameof(SettingsProfile.TestPortRange),
                $"Port range {value.Start}-{value.End} must start at or above {MinimumUnprivilegedPort} (unprivileged ports)."));
        }
    }

    private static void ValidateRole(List<SettingsValidationError> errors, AgentRole role, RoleSettingsOverride roleSettings)
    {
        RequireAtLeast(errors, $"Roles.{role}.{nameof(roleSettings.TimeoutSeconds)}", roleSettings.TimeoutSeconds, MinimumPositiveValue);

        if (roleSettings.PromptTemplate is { } template)
        {
            errors.AddRange(PromptTemplateValidator.Validate(role, template));
        }
    }

    private static void RequireAtLeast(List<SettingsValidationError> errors, string field, int? value, int minimum)
    {
        if (value < minimum)
        {
            errors.Add(new SettingsValidationError(field, $"Must be at least {minimum}, but was {value}."));
        }
    }
}
